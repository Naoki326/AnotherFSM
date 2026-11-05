# C# 消费者与宿主配置（#21）

[English](consumer-host.EN.md) · [核心契约](typed-context.md) · [验收证据](implementation/21-consumer-host-migration.md)

## 脚本与聚合由宿主显式配对

真实示例：`demos/BusinessContext.fsm`、`demos/DemoNodes/BusinessFlow.cs`。
`BusinessQueryNode` 只依赖 `IQueryBusiness`，`BusinessProcessNode` 只依赖 `IProcessBusiness`；同一个 `DemoJob` 实现两者。查询写入 41，处理读取同一对象并更新至 42。节点只读自己的 `Context`，取消/暂停读 `Control`，触发信息读 `TriggerEvent`。

```csharp
var engine = new FSMEngine(factory);
engine.CreateStateMachine(script); // 也可直接 C# 或 Fluent Build；Module/use 真实展开
var flow = engine.BindContext<DemoNodes.DemoBusinessInput>(); // 明确选择类型，不按文件名猜测
using var prepared = flow.Prepare(new DemoNodes.DemoBusinessInput()); // 每次显式实例
using var executor = await prepared.StartAsync("Start", "EndEvent");
await executor.Execution.Completion; // 真退出后，才可再次 Prepare
```

多来源在聚合类内声明关系，并由宿主传方法组：

```csharp
public static void Configure(FSMContextBindings<OrderInput> b)
{
    b.DefaultSource(x => x.Job); // 或 Default<IQueryBusiness>(x => x.Job)
    b.Node<IProcessBusiness>("Area.B.Process", x => x.Other);
}
// engine.BindContext<OrderInput>(OrderInput.Configure)
```

唯一兼容直接公开属性自动成为来源；多候选必须明确默认。节点覆盖使用真实展开限定名，优先于默认，不随转移覆盖。即使只从局部入口启动，也装配整个引擎、准备全方案必要来源；null 不回退。聚合属性用只读、private set 或 init，不用普通 public set。源码位置未知时错误不编造行列。

## 同一个共享 Runtime、两个运输适配器

`EngineManager` 接收可选 `HostContext`；`RuntimeSession` 同样转交它：

```csharp
var pairing = HostContext.Create<OrderInput>(() => CreateInputForThisRun(), OrderInput.Configure);
var manager = new EngineManager(logger, factory, context: pairing);
// 或 new RuntimeSession(factory, publishJson, context: pairing)
```

这是 C# 配置，不是通用依赖容器：不扫描聚合类、不推断文件名、不按本次运行时子类猜来源。`Prepare` 每轮重新装配实际完整图，然后借用本次输入。API `Program.cs` 与 Browser `Program.cs` 明确安装相同的 `DemoBusinessHost.Create()`，为这个可编辑 Demo 工作区选定 `DemoBusinessInput`；编辑脚本不会改变聚合类型。没有配置的有依赖图在候选装配阶段拒绝。无依赖节点不创建空业务对象，走明确无依赖执行路径。

`DemoBusinessHost` 每轮创建新的 Job；`Approval` 默认缺失，使用 `BusinessApproval` 的图在准备前明确失败，零节点运行。`ExecutionStatus.businessResult/businessRun` 只是固定、安全的 Demo 结果投影；业务实例和 CLR Type 不过 JSInterop。前端只有原有 start/end 参数，没有依赖配置面板、任意 JSON 或 CLR 名称。

## 普通 start 与显式替换不同

- start 遇到准备中/准备占用、运行、暂停、停止清理或 Finished 尚未真退出，立即拒绝。不停止旧执行、不换绑定、不清 UI 状态或日志；先显式 `await StopExecution()`，再请求本次新输入。
- 宿主来源工厂、用户初始化不持宿主状态锁；冲突 start 不排队。停止和显式 Import 会等待启动准备屏障及旧执行真实清理。绑定引用与退出权威仍在核心，不复制算法。
- Import 先解析并校验候选完整绑定，再退役旧执行、等待真正退出和旧图释放后返回；无效候选不停止原执行。替换时断开旧通知，不能把此许可扩张为普通 start。
- 若订阅必须早于首节点，使用 `prepared.StartAsync(start, endEvents, beforeStart)`；支持多个结束事件，保留宿主顶层 End 规则，Module 内 End 不是全局结束节点。

## 三种工厂与浏览器安全边界

- API：`AssemblyScanningNodeFactory` 扫描真实程序集；不要求 Autofac。
- Browser：`BrowserNodeFactory` 封闭注册 Start/End/Idle/Sleep/Accumulate 和三个 Business 节点；不开放动态 CLR 注册、程序集扫描、设备插件或外部文件模块。内联/已注册 Module 可用。
- Autofac：`AutofacNodeFactory` 使用真实 `ResolveKeyed<IFSMNode>`；推荐 `RegisterType<BusinessQueryNode>().Keyed<IFSMNode>("BusinessQuery")`。不准确的 delegate 元数据不是业务依赖声明，BindContext 会拒绝声明/实际节点不一致。

三者共用核心全引擎需求校验。错误包含实际节点限定名、类型键、声明/实际类型、需求和根因；实例来源错误还包含来源路径。Module 的 Input/Output、真实多发布者去重、Branch 不重绑、实例限定事件和 Namespace 保持不变。

Release WASM 保留 AnotherFSM/DemoNodes/FSMDemo.Runtime 反射元数据，同时裁剪平台库；真实 Chrome 验证 getter、局部接口、DSL、DTO 和 JSInterop。WASM 初始化失败只明确重试，绝不回退 API；测试先在后端离线时执行并监控零 REST/SignalR 业务请求。

## 生成消费者与暂存入口

`BusinessModules.FSMScript` 是实际 AdditionalFile。生成类只持有 Engine 与节点/事件访问器；宿主显式 `generated.Engine.BindContext<DemoBusinessInput>()`。现有生成器无需修改，更没有新增业务绑定生成器。`tools/test-typed-context.sh` 包括错误生成消费者输入的真实 CS1503 证据。

供 #22 收缩的暂存清单：`IFSMNode.Context`、非泛型基类旧 Context、`FSMNodeContext`/泛型包装及 Data/As、`FSMExecutor` 构造/四个 Restart 重载、无轮次 Handle/engine.PublishEvent 旧入口（已拒绝不安全外部投递）、旧名 `ReinitGroupNode`（实为所有 injectable 节点服务，不能按名称删除）。Runtime 和既有生成无依赖图仍使用无 Data 的构造/Restart 驱动；新业务图只用 BindContext/Prepare/Execution。FakeNode 的旧 Context 属性仅满足暂存 IFSMNode 接口，不传播业务。Demo/生成节点不再读 Context.Token/Data 或调用者强转。

#20 的共享/协调启动不属于本票；不新增共享 UI、WPF/WebView2、AOT、Worker 或多线程部署要求。宿主 .NET 回归不是浏览器证据；逐层结果见验收记录。
