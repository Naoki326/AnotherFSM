# 异构上下文使用体验：可抛弃原型

本目录为[异构上下文的使用体验是否足够简单且类型安全？](https://github.com/Naoki326/AnotherFSM/issues/12)的讨论资产，不是生产实现。权威结论在该工单的 resolution comment；这里保留可复核的样例、探针与交互证据，不复制完整规格。

## 打开与重放

- 双击 `context-prototype.html`：修订后的单文件演示，无依赖、无持久化；支持自由操作与引导场景。
- `context-prototype-v1.html`：首次讨论的历史版本，含后来被否决的逐事件 `using link` 形状，**不是最终推荐接口**。早期浏览器核验针对该版本的行为模拟。
- `bash .scratch/wayfinder/12/prototype/csharp/run-probe.sh`：从仓库根目录运行独立 C# 探针，见 `csharp/README.md`。
- `proof/`：修订版浏览器实际操作截图；`browser-evidence.json` 记录两轮检查，其中 localhost URL 只是当时的本地演示地址，不是远端部署。

演示无真实并发、C# 类型系统或 DSL 编译器。C# 探针真实编译/运行，但不引用 AnotherFSM，不覆盖执行器或消息机制。两类证据不能互相替代，更不能视为生产验收通过。

## 反馈后的调用形状

下述 `Build`、`Prepare`、`Connect`、`Start` 均为未编译的接口讨论，不宣称当前仓库提供这些 API。

```csharp
// 同一业务对象实现不同局部 Context；Node 不依赖聚合类。
class FlowContext
{
    public JobData Job { get; init; }
    public JobData OtherJob { get; init; }

    public static void Configure(Bindings<FlowContext> bindings)
    {
        bindings.DefaultSource(x => x.Job);
        bindings.Node<IQueryContext>("CheckB.Query", x => x.OtherJob);
    }
}

// 流程图先构建；类型与脚本显式配对，不给 DSL 增加绑定配置。
var engineA = Build<FlowContext>(File.ReadAllText("flow.fsm"));
var engineB = Build<OtherFlowContext>(otherDsl);
var engineC = Build<ThirdFlowContext>(thirdDsl);
var a = engineA.Prepare(new FlowContext { Job = jobA, OtherJob = jobB });
var b = engineB.Prepare(inputB);
var c = engineC.Prepare(inputC);
Connect(a, b); // void：框架持有连接，没有 link / using
Connect(b, c); // 双向、传递共享业务事件，不共享控制信号
Start(a, b, c); // 接收端先全部就绪，再放行节点
```

只有唯一兼容来源时无需 `Configure`；`private set` 同样可以阻止调用方直接重新赋值。来源属性限制不冻结业务对象内部数据，执行期间仍使用已固定的对象引用。

## DSL 宿主示意（未运行）

语法形状参考本工作树 `demos/FSMDemo.Frontend/src/demo.fsm` 和 `tests/FSMScriptAnalyzerTest/SubFlow.fsm`。以下 `Query` / `Process` 是**拟议的节点工厂类型键**，需要宿主注册相应局部 Context 节点；本探针没有注册真实工厂，也没有执行此 DSL。

```text
module Check {
    input Query;
    output Done;
    def Query(Query) { 1->Next; }
    def Process(Process) { 1->Done; }
    def Next as event;
    def Done as event;
    Next->Query to Process;
}
import Check;
use Check as CheckA;
use Check as CheckB;
CheckA.Done->CheckB;
```

宿主用 `Build<FlowContext>(dsl)` 配对；`CheckA.Query`、`CheckA.Process`、`CheckB.Query`、`CheckB.Process` 参与同一平面绑定校验，只有 `CheckB.Query` 覆盖到 OtherJob。这里不引入第二个 Module 运行时引擎。直接 C# 装配采用相同聚合绑定方案，独立探针展示其类型化调用，但不是产品构建器。

## 未定边界

[共享业务事件与协调启动如何保持执行轮次边界？](https://github.com/Naoki326/AnotherFSM/issues/15)承接事件身份、准备期占用、批量启动失败与拓扑生命周期问题。本页演示只选了简单成功路径：

- `Prepared` 是 UI 阶段，不新增生产 FSMState；固定引用在演示 Start 时读取，不预选正式捕获时机。
- `Start` 的待启动集合、准备期占用检查、退出删除连接边，是模拟所需的局部简化，不是用户已确认的故障/拆分政策。
- E 是符号占位，不决定按名称还是 EventID 识别；接收计数不等同于真实图已发生转移。
- 同一通知去重不等于可靠投递；业务主动响应而产生的新通知仍是新的通知。
- 不模拟运行中改拓扑、暂停消息、失败回滚、后台任务清理或并发竞争。

未改生产代码；未提供 ParallelModule、父子执行、完成汇合、持久订阅、离线缓存或重放。原型保存在独立 throwaway 分支，不合并到主分支。
