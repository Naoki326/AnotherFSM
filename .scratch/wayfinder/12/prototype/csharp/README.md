# 异构上下文使用体验：独立 C# 编译/运行探针（THROWAWAY）

**问题**：聚合类组合业务对象、节点只依赖局部 Context 的调用形状，能否在普通 C# 中表达共享、按次固定绑定及显式来源的静态类型约束？

这是供 HITL 阅读/修改调用代码的同步控制台草图，不是真实库、测试套件、FSMEngine 或容器。没有生产项目引用、NuGet 包依赖、HTML、DSL 编译器或并发运行时。观察成立不等于用户已接受 Interface，也不等于 AnotherFSM 已实现目标。

## 依据与边界

已读 `docs/agents/domain.md`、`GLOSSARY.md`、ADR-0001、prototype/codebase-design 技能文件；通过只读 `gh issue view ... --json title,body,comments` 读取：

- [C# 与 DSL 如何装配异构节点并保证类型错误及时暴露？](https://github.com/Naoki326/AnotherFSM/issues/11#issuecomment-6645409323)：聚合类、自动候选、显式默认与节点覆盖、按次固定、分阶段校验。
- [移除运行时组合后，独立引擎与执行轮次如何避免状态及事件串扰？](https://github.com/Naoki326/AnotherFSM/issues/14#issuecomment-6647973766)：移除 GroupNode/ParallelNode，保留普通 Module 的编译期展平；独立引擎与执行隔离不在本探针范围。

本探针是主代理委派的 C# 子任务，仅在本目录新增或修改文件；HTML 行为演示由主代理另行提供。子任务未 commit/push、未写工单、未修改生产代码。原型归档与工单结论由主会话在收到用户反馈后处理，不将子任务边界误作用户的新要求。

`CheckA.Process`、`CheckA.Query`、`CheckB.Query` **是手工提供的模拟 Module 展平结果**。没有创建/解析 Module，没有运行真实 DSL，也没有验证其工厂或源码位置。本 README 不提出新的 DSL 语法。

## 本轮更新：来源不可外部重新赋值，业务内容仍可变

用户新确认的是**聚合类持有业务对象的来源属性**不可被外部重新赋值，不是冻结 `JobData.Tasks`：

- 旧版 `SingleJob.Job`、`TwoJobs.Job/OtherJob` 使用普通 `public set`，并直接从外部替换 Job；该调用形状已撤回。
- 新版 `SingleJob.Job` 使用 `public get/private set`，通过构造函数初始化；`TwoJobs.Job/OtherJob` 使用 `public get/init`，通过对象初始化器赋值。
- `Bindings.Resolve` 对**选中的来源**统一检查 setter：普通 public set 在装配阶段拒绝；识别 setter 返回参数上的 `IsExternalInit` required modifier，避免把 public init 当作普通 setter。自动候选、Default、DefaultSource、Node 的选定结果都经过这一检查。不因 public set 偷偷改选其他来源。
- getter-only 也兼容此检查，但没有新增该形状的独立运行示例。不检查未选中的来源，也不宣称检测所有间接赋值渠道或复杂成员路径。
- `ReplaceInsideForProbe` 仅用于演示类内部仍能通过 private set 换引用，**不是推荐生产公开 Replace 方法**。即使类内部更换属性，本次绑定仍读取原 JobData，下次执行才读取新对象；推荐正常调用仍是为新执行提供初始化好的聚合实例。
- Process 仍对 `Context.Tasks.Add("processed")`，Query 可见修改：来源属性的赋值限制不是深度不可变，也不禁止更新业务对象内部成员。

新增 `BAD_REPLACEMENT` 实际编译反例，以及普通 public set 来源装配失败且零节点运行的演示；原 `BAD_BINDING` 和其他观察保留。未增加事件共享、协调启动或其他并发运行时；这些交互由主代理 HTML 示意。

## 一条命令重放

在仓库根目录（Git Bash，SDK **10.0.300**）：

```bash
bash .scratch/wayfinder/12/prototype/csharp/run-probe.sh
```

脚本先分别做 BAD_BINDING、BAD_REPLACEMENT 两次预期失败构建，再正常构建和运行；覆盖 `logs/` 中对应证据。脚本本次退出 **0**。所有 dotnet stdout/stderr 均重定向到本目录日志。项目固定 SDK、仅编译两个显式 C# 文件，禁用祖先 Directory.Build 导入；NuGet 源清空，CLI home / package cache / bin / obj 都位于本目录。不需要构建仓库 solution。

实际命令（工作目录为本目录）与退出码：

| 命令 | 退出码 | 证据 |
|---|---:|---|
| `dotnet --version` | 0 | `logs/sdk.log`：10.0.300 |
| `dotnet build Probe.csproj --nologo -p:DefineConstants=BAD_BINDING` | **1（预期）** | `logs/bad-binding-build.log` |
| `dotnet build Probe.csproj --nologo -p:DefineConstants=BAD_REPLACEMENT` | **1（预期）** | `logs/bad-replacement-build.log` |
| `dotnet build Probe.csproj --nologo` | 0 | `logs/normal-build.log`：0 warning / 0 error |
| `dotnet run --project Probe.csproj --no-build` | 0 | `logs/normal-run.log` |

退出码原始记录：`logs/exit-codes.log`；脚本退出码见 `logs/runner.log`。BAD_BINDING 在 `Program.cs:66–67` 的 **Default 和 Node 两行**分别产生 CS0029、CS1662：`string` 不能隐式转换为 `IQueryContext`。BAD_REPLACEMENT 在 `Program.cs:71–72` 分别产生 **CS8852**（init 初始化后重新赋值）、**CS0272**（private setter 不可访问）。都是实际 C# 编译器拒绝，不是运行时模拟或依赖恢复失败。本轮 bash 执行显式 timeout 为 120 秒。

## 调用代码在哪里

- **`Program.cs`**：可直接编译的完整调用及七段引导演示。`Show` 仅令观察不成立时退出失败，`Reject` 展示预期错误及节点调用计数；不是测试框架。
- **`BindingProbe.cs`**：139 行绑定草图、两个局部接口、两个真实不同的节点类及一个无业务依赖节点。连同 `Program.cs` 共 249 行 C#（`logs/source-lines.log`）。
- **`Probe.csproj` / `global.json` / `NuGet.Config`**：独立构建配置。
- **`run-probe.sh` / `logs/`**：可重放命令与实际证据。

唯一直接属性不需要配置（下列形状在 `Program.cs` 中实际使用）：

```csharp
var automatic = new ProbeFlow<SingleJob>(new Bindings<SingleJob>(),
    new ProcessNode("CheckA.Process", context => { /* 修改后观察 */ }),
    new QueryNode("CheckA.Query", context => { /* 读取同一 JobData */ }));
automatic.Run(new SingleJob(job));
```

多候选关系集中在聚合类；不用每次执行逐节点 Set：

```csharp
public static void Configure(Bindings<TwoJobs> bindings)
{
    bindings.DefaultSource(x => x.Job);
    bindings.Node<IQueryContext>("CheckB.Query", x => x.OtherJob);
}
```

本草图的宿主**显式调用** `TwoJobs.Configure(bindings)`，没有自动发现 Configure 的机制。`DefaultSource` 将兼容接口统一指向 Job；Node 限定名覆盖优先。单接口显式形状 `typed.Default<IQueryContext>(x => x.Job)` 也已编译并运行。

编译反例由 `#if BAD_BINDING` 包裹：

```csharp
var bad = new Bindings<NoData>();
bad.Default<IQueryContext>(x => x.Text);
bad.Node<IQueryContext>("CheckB.Query", x => x.Text);
```

两个方法的参数均为 `Expression<Func<TAggregate, TContext>>`，因此编译器检查**表达式返回值与所声明 TContext** 的兼容性；不能据此声称编译器知道名为 CheckB.Query 的动态节点实际需要什么。

外部重新赋值的反例由独立 `#if BAD_REPLACEMENT` 包裹，正常初始化示例位于此前的调用代码：

```csharp
pair.Job = replacement;       // public init：CS8852
aggregate.Job = replacement;  // private set：CS0272
```

两组反例独立构建，避免一组错误掩盖另一组。

## 本次实际覆盖

| 场景 | 日志中的观察 |
|---|---|
| 唯一直接 Job 属性自动成为候选 | Process/Query 的 PLAN 都指向 Job；节点分别只见 IProcessContext/IQueryContext |
| 同一对象共享且内部数据可变 | ReferenceEquals 为真；Process 添加 `processed` 后 Query 可见 Tasks |
| 类内部替换聚合 Job 不换绑 | A 执行中通过原型专用方法把 private-set Job 改成 B；Query 仍读取 A，B 当时未被修改 |
| 下次执行重新取对象 | 同一方案下一次读取 B；再传新聚合实例时读取 C |
| 多候选未消歧 | 装配拒绝，诊断含节点、接口、Job/OtherJob 及声明类型 |
| DefaultSource 与节点覆盖 | 默认 Process/Query 共享 Job；CheckB.Query 读取独立且未修改的 OtherJob |
| 类型化 Default 正例与 Default/Node 反例 | 正常运行观察 Job；BAD_BINDING 实际编译失败 |
| private set / init 来源及外部赋值限制 | 两种来源均正常装配运行；BAD_REPLACEMENT 的两行外部重新赋值实际编译失败 |
| 普通 public set 来源 | PublicSetJob 的 Job 在装配阶段拒绝，含聚合类型、节点、接口、来源类型与 setter 原因；节点运行数为 0 |
| 缺失绑定 | 装配构造失败，无可运行的新方案；不是自动当作无业务需求 |
| 无依赖节点免绑定 | NoData 聚合无业务来源，OnlyControl 仍能运行 |
| 聚合 null、后续节点来源 null、默认来源 null | 启动拒绝；包括排在最前的免绑定 Start 在内，节点运行数均为 0；默认 null 不回退 |

装配只保存属性来源，不捕获业务实例。Run 先读取完整传入平面列表的必要属性，每个 PropertyInfo 读取一次，再统一检查 null，然后才调用节点。缓存值贯穿本次同步调用，节点结束后清除其局部 Context。调用方只提供类型化表达式和聚合实例；实现内部有反射及类型擦除，不要求调用方通过 object/dynamic/强转取依赖。

## 明确不证明 / 已知刻意省略

**不能将草图直接作为生产绑定器**，尤其以下规则未实现，不能因为演示成功而视为已验证：

- **冲突检查未实现**：重复 Default、重复节点覆盖、多个 DefaultSource（包括不同来源支持同一接口）不做完整拒绝。草图字典赋值/单字段会覆盖旧配置；这是未完成处，**不是允许最后注册者获胜的决策**。Default 与 DefaultSource 冲突也未核对。
- **来源表达式检查不完整**：仅提取直接属性，最多剥离一层 Convert；不是合法性证明。没有验证转换/用户定义转换、静态显式来源、投影、条件、方法调用、包装或复制等完整约束。许多表达式会因简易形状匹配被拒绝，但没有逐类编译/运行证据。显式嵌套成员路径也未支持；不宣称实现前置装配决策的全部合法来源。
- **节点注册契约未实现**：未知限定名、声明接口与实际节点需求不匹配、重复节点名、工厂类型键/元数据不一致、错误类型的内部来源都未完整验证。错配可能被忽略或在节点运行时强转失败；这不是允许生产系统延迟拒绝的结论。
- 自动扫描使用直接公开可读实例非索引属性的声明类型；未做嵌套对象、集合、运行时子类型、getter 副作用等反例演示。没有读取性能、复杂路径共享身份或异常 getter 的保证。
- 全量预读仅覆盖**手工传入的平面列表**；没有真实 FSM 图、不可达节点/局部入口、转移、注册表、DSL 诊断位置或模块扩展的验证。
- 没有独立引擎并发、节点实例归属、重叠启动拒绝、执行句柄、事件隔离/连接、暂停/停止/退出屏障、异步清理或重启机制。同步多次 Run 只证明顺序取新数据，不证明前置执行隔离决策的生命周期契约；没有 Group/Parallel 的替代实现。
- 未演示错误聚合类型的编译反例、动态入口、业务就绪检查；null 的诊断不是完整生产错误格式。Tasks 的线程同步仍是业务问题。

**HITL 留痕**：用户已选择保留默认来源与节点例外，并要求来源属性禁止外部重新赋值；这些判断来自真实对话，不来自探针通过。完整反馈及最终接口调整以[异构上下文的使用体验是否足够简单且类型安全？](https://github.com/Naoki326/AnotherFSM/issues/12)的 resolution comment 为准。
