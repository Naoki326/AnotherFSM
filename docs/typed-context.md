# 类型化局部 Context（#18）

[English](typed-context.EN.md)

这是核心 C# 新入口；不是整个 Context 解耦迁移的最终发布。完整 DSL/Fluent/Module/生成消费者、三种工厂及 API/WASM 配对由 #21 接入本入口；#19 负责执行占用、真正退出和轮次隔离，#20 负责事件共享与协调启动，#22 删除过渡入口。

## 节点只认识局部业务接口

```csharp
interface IQuery { string? Tasks { get; set; } }
interface IProcess { string? Tasks { get; } }
sealed class Job : IQuery, IProcess { public string? Tasks { get; set; } }

sealed class QueryNode : BaseFSMNode<IQuery>
{
    protected override Task ExecuteAsync()
    {
        Control.CheckPause();              // 控制入口，不在业务 Context 中
        Context.Tasks = "queried";        // 直接局部接口，没有 Data 包装
        PublishEvent(1, "query finished"); // 载荷不是业务 Context
        return Task.CompletedTask;
    }
}
sealed class ProcessNode : BaseFSMNode<IProcess>
{
    protected override Task ExecuteAsync()
    {
        Console.WriteLine(Context.Tasks);
        Console.WriteLine(TriggerEvent?.EventContext);
        PublishEvent(1);
        return Task.CompletedTask;
    }
}
```

`BaseFSMNode<T>`、`EnumFSMNode<T>`、`AsyncEnumFSMNode<T>` 都通过只读 `Context : T` 借用业务对象。节点不认识聚合类型。无依赖节点使用非泛型家族，明确声明无业务依赖，不需要空业务接口。Yield 接收 `FSMExecutionControl`，不能访问业务 Data 或触发载荷。

## 一次装配关系，每次执行一个聚合实例

```csharp
sealed class FlowInput
{
    public Job Job { get; init; } = new();
    public Job Other { get; init; } = new();

    public static void Configure(FSMContextBindings<FlowInput> bindings)
    {
        bindings.DefaultSource(x => x.Job); // 一次覆盖所支持的需求接口
        bindings.Node<IProcess>("Work.Process", x => x.Other);
    }
}

// engine 已通过普通 C# 节点/事件/转移入口构造完整图；工厂的
// GetNodeType(typeKey) 必须与实际节点类型和局部需求一致。
var flow = engine.BindContext<FlowInput>("Query", "Done", FlowInput.Configure);
var input = new FlowInput();
var prepared = flow.Prepare(input); // 所有必要来源现在读取、固定；节点尚未初始化/运行
using var executor = await prepared.StartAsync();
await executor.ExecutorTask;       // 本票沿用执行器；退出/占用保障由 #19 完成
```

只有一个兼容直接属性时，省略 Configure：`engine.BindContext<SingleInput>("Query", "Done")`。也可装配时不指定入口，再调用 `prepared.StartAsync("Local.Entry", "Done")`；局部启动仍校验全方案。每次执行仅提供聚合实例；关系不捕获某次业务实例。节点例外使用引擎实际限定名（不是类型名），不会被上游转移覆盖。

`FSMContextPreparation` 目前是**固定引用的数据准备结果**，不是已获得引擎占用的生命周期句柄。不要并发启动/复用同一引擎或 Node 实例；#19 将把同一准备路径接入占用/释放保障。顺序新执行先等待旧执行退出，再显式 `flow.Prepare(input)`。旧 `RestartAsync`/`Data` 不能启动有业务需求的节点，也不能重启类型化执行。改变图的节点集合后必须重新 BindContext。

## 规则和失败阶段

- 优先级：限定节点来源 > 显式接口默认 `Default<IQuery>(x => x.Job)`（包括 `DefaultSource` 简写）> 唯一兼容直接属性。默认与节点例外可以共存；同级重复、默认冲突、缺失和歧义报错，不是最后注册获胜。
- 自动扫描仅使用直接公开可读、非静态、非索引器属性的**声明类型**。不扫描字段/集合/嵌套对象，不看属性名字、顺序或运行时子类。
- 显式表达式仅允许从参数开始的已有公开属性路径，例如 `x => x.Nested.Job`。不允许转换、条件、方法调用、捕获外部对象、构造/复制/投影或索引器；来源必须声明为引用类型，不能通过装箱复制业务数据。
- 来源路径上的普通 `public set` 在装配时拒绝；`init`、`private set`、仅 getter 可以使用。C# 编译器拒绝外部后续赋值；框架不是验证所有聚合类的源码生成器。
- `BindContext` 校验**整个引擎**和工厂声明/实际类型/需求元数据，不可达节点也不例外。装配失败没有可执行方案。未来宿主必须复用这个校验器，不另写绑定算法。
- `Prepare` 在任何节点运行之前解析全方案必要来源；聚合或必要来源 null、getter 异常均失败，不回退候选。动态宿主使用单独命名的 `PrepareDynamic(object?)`，错误类型同样在节点运行前拒绝。正常 C# 使用类型化 `Prepare(TInput)`。
- 诊断提供已知的节点限定名、需求、来源路径、声明/实际类型及根因；未知的 DSL 行列不伪造。装配时尚无运行实例的实际类型不作猜测。
- 来源身份按完整属性声明链区分，不按名称路径合并；继承中隐藏的同名属性可以是独立来源。名称路径仅用于诊断。一次准备中同一来源用于多接口仍是同一个对象。类内后来换掉属性引用，不改变本次绑定；业务成员（包括集合引用）仍可更新。合法业务对象字段是否就绪由节点判断。

## 控制、触发和所有权

`Control.Token` / `Control.CheckPause()` 处理取消/暂停；`TriggerEvent` 是独立触发入口。转移给目标更新触发信息；暂停继续保留原事件，内部继续信号不是业务事件；新执行所有节点先清空触发信息再按节点契约初始化。需要长期保存载荷时，由业务显式写入 Context。

循环重入和暂停继续不清空业务对象。框架不复制、回滚或 Dispose 调用方资源；调用方持有和释放它们。节点临时资源由节点清理。本次私有状态在 `InitBeforeStart` 等节点契约中显式初始化，框架不反射清空字段；跨执行累计由调用方显式持有。

## 过渡入口与验证

`IFSMNode.Context`、非泛型基类的旧 `Context`、`FSMNodeContext[<T>]`、旧 `FSMExecutor` 构造/RestartAsync 仍暂存，以保持既有无业务依赖宿主、Demo 和生成消费者构建。新路径不读旧 Data。它们不是最终兼容承诺；旧样例不适用于类型化业务 Context。

真实执行/装配测试：`tests/StateMachineTest/TypedContextFacts.cs`、`TypedExecutionFacts.cs`。真实编译正负例：`bash tools/test-typed-context.sh`。红绿日志与范围说明：`.scratch/ticket-18/implementation.md`。宿主 .NET 回归不等于真实 Release WASM 验收。
