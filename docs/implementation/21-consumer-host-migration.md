# #21 消费者与宿主迁移 / Consumer-host migration

## 首次实施范围与基线（后续 P1 修复见末节）

分支 `pi-web-agent-d1937fe7-ef0e-48d3-a545-869d89c81b2f`；唯一工作树 `C:/Work/AnotherFSM-worktrees/pi-web-agent-d1937fe7-ef0e-48d3-a545-869d89c81b2f`。
开工 status 干净；HEAD 与 merge-base 都为 `4063178bca93749ce966988920d349852e80108f`。
仅实施 #21，不委派、不启动模型 CLI/代理、不建第二工作树、不改变 #20 分支、不 push/关闭票。

生产配置及双语使用说明：[中文](../consumer-host.md) / [English](../consumer-host.EN.md)。
原始日志已另存 `C:/work/anotherfsm-context-19/evidence-21/`，不依赖此隔离工作树生命周期。首次交付受测代码提交为 `103980b33feefc13ded2e65bcb43ec62ccf02831`；后续 P1 生产修复与重新验收见末节。最新集成 tip 为 `6f43cf9bcb793ba899713cf59e9b7f4d3e6fe14d`，已合入本分支；最终 merge-base 为该 tip。

## 阶段提交（按验收族）

- `451458c`：多结束事件与首节点前订阅的最小公开兼容扩展；原 #19 单结束事件调用形状保留。
- `f4d0097`：普通 start 不再 Retire 旧执行。
- `17041c3`：C# 显式聚合配对、真实业务节点、每轮输入、API/Browser 配置及固定结果 DTO。
- `5d31a0e`：真实工厂创建错误保留节点身份/根因。
- `0fad971`：类型化启动支持宿主独立结束事件，不为失败 start 改图。
- `20ef949`：直接 C#/Fluent/真实 DSL/Module、三种实际工厂和完整来源集成。
- `36bb1cf`：宿主准备/初始化不持状态锁；普通 start 立即拒绝；显式替换等待准备与真退出。
- `accba30` / `e6fe50b`：只读核心占用快照及宿主在请求输入前拒绝已有准备。
- `b7e096b`：Demo 控制入口、生成消费者业务执行、真实编译正负例。
- `0a69c4d`：失败 start 不清 UI 日志，同 EditorRuntime 的真实 Release WASM/API 业务合同。
- `ca2112a` / `4bffb85`：会话准备冲突、无效候选保留暂停执行、生成类型访问器真实编译/查询/执行。
- `aafa6b2` / `e5a0680`：Finished 未真退出回归；初版测试误等尚未请求的取消清理，改为先观察 Finished 再显式取消，未修改核心退出语义。
- `f2fabd8`：Finished 不是退出证明，UI 仍允许显式 Stop。

## 合入 #20 前的真实 red → green

所有接缝由父规格预先确认：公开核心装配/执行、同 EditorRuntime API/真实 Release WASM、真实 C# 编译器。未另行组织独立审查。

| 行为 | Red（原始日志） | Green |
| --- | --- | --- |
| 类型化消费者保留多个结束事件并订阅首节点 | `core-extension-red.log`：CS1503/CS1660 | `core-extension-green.log`：核心选集 79 通过 |
| 普通 start 不得隐式替换旧执行 | `start-red.log`：第二次启动成功、没有 error | `start-green.log`：宿主 .NET 27 通过 |
| 显式 C# 宿主配对及每轮实例 | `typed-host-red.log`：缺宿主配置/结果 DTO 的编译错误 | `typed-host-green.log`：生产 scanning 工厂真实业务结果 42 |
| 错误类型键保节点限定名与根因 | `factory-diagnostic-red.log`：直接 C#/DSL 两例失败 | `factory-diagnostic-green.log`：4 通过 |
| 已有准备时失败 start 不增添结束事件 | `prepared-host-red.log`：事件集合被改变 | `prepared-host-green.log`：15 通过 |
| 准备中立即拒绝第二 start，Stop 等待准备 | `host-preparing-red.log`：第二 start 被宿主锁阻塞，超时 | `host-preparing-green.log`：API 55 通过；session 27 通过 |
| 外部准备占用时不提前请求本轮来源 | `occupied-input-red.log`：来源 provider 被调用 | `occupied-input-green.log`：宿主选集 6 通过 |
| 生成业务流程及类型化节点/事件访问器 | `generated-red.log`、`generated-accessor-red.log`：真实编译缺失生成类型/访问器 | `generated-green.log`、`generated-accessor-green.log`：编译、查询、执行成功 |
| 失败 start 保留 UI 旧日志 | `browser-red-2/wasm-results.json`：11 过、1 失败及 trace | `browser-green/`：WASM 12/API 7 通过 |
| Finished 仍提供显式 Stop | `finished-ui-red/wasm-results.json`：11 过、1 失败及 trace | `browser-final-2/`：WASM 12/API 7 通过；合并前最后联合命令工具报 120s 超时，结果 JSON 和 runner success 已落盘，随后核验 5079/5180 均关闭，最终合并后仍须独立重跑 |

`consumers.log` 的三次失败来自测试 fixture 使用错误的 Namespace 大小写和文件模块缺 wrapper；修正真实 DSL 后 `consumers-green.log` 10 通过，未伪称生产缺陷红灯。

## 合并前验证边界

`solution-final-test-2.log`：核心 232、API 56、宿主 .NET 30，全过。`solution-final-release-2.log`：Release solution 0 错误、62 警告。`compiler-final.log`：正例和 8 个真实编译负例，包括生成消费者 CS1503。前端 test 12、typecheck/lint 均过。以上不是 #20 合并后的最终验收，合并后另记。

环境实际问题及处置：初次 npm ci 受机器 omit=dev 配置影响，改用 `npm --script-shell='C:/Program Files/Git/bin/bash.exe' ci --include=dev`；继承 PORT=30141 与 runner 默认不同，显式 `PORT=5180`，未杀已有服务。JRE 为 `C:/Users/Naoki/.jre/jdk-11.0.26+4-jre`。Release browser 由 `RUNTIME_CONTRACT=true bash tools/build-browser.sh` 构建，再由 `PORT=5180 node tools/run-browser-tests.mjs` 真实 Chrome 执行。宿主 .NET 从未冒充浏览器。

现有依赖安装报告 18 个 npm vulnerabilities（11 moderate、7 high）；未在消费者票擅自升级 lock。已有反射/Reactive 裁剪、nullable/打包警告保留，真实 Release WASM 结果而非警告推断作为兼容证据。

## 合入 #20 后的兼容处置（最终）

在 `31ab779` 阶段提交干净后合并集成 `6f43cf9`。唯一文本冲突为 `FSMContextBinding.cs`，没有复制/覆盖整文件，没有改 #20 分支或删除其测试。

- 保留 `Member.Starting`、共享 Sync、WithEntry、全 pending 集合检查、接收资源失败回退/保留占用/重试、固定完整名与真退出。
- #21 多结束事件只作为 preparation 的选定结束配置，由 `CreateExecutor` 消费；仍经过同一个 `FSMEventSharing.Start` 屏障，不另建启动算法。
- 首节点前观察者安装在 `PrepareReceiver` 内、共享锁外，放行前运行；失败走 #20 原有 receiver rollback，不消耗有效 preparation。
- 保留 #20 的 `PrepareContext → CommitPreparedContext → RunPreparedContext`；未恢复已删除且原无调用的 `StartContext`。
- 显式 Stop/Import 在宿主准备期间使观察者安装失效时，#20 会用 receiver exception 包装根因；宿主仅识别自己的 `StartRetiredException` 根因并返回本次未启动，不吞别的错误。
- 新增公开整合回归 `HostObserverFailureKeepsPreparationRetryableAtTheSharedReceiverBarrier` 和 `HostMultiEndStartCannotBypassTheCompletePendingReceiverSet`。两例首次已绿，不伪称生产红；与既有首节点/多结束事件用例合计 3 过。#20 所有 30 例原样保留，全部通过。

### 最终真实命令与各层结果

所有命令均在本隔离工作树执行，bash timeout 15–120 秒；最终构建、runner 分开运行，均明确退出 0，没有将之前的超时当作最终成功。JRE 环境按上文；没有后台代理或 sleep 轮询。

| 命令 | 合并后结果 | 原始日志（`evidence-21/raw/` 下） |
| --- | --- | --- |
| `dotnet test AnotherFSM.sln --nologo -p:NuGetAudit=false` | **核心 264 / API 56 / 宿主 .NET 30 全过，0 失败** | `postmerge-solution-test.log` |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --no-build --no-restore --nologo --filter FullyQualifiedName~EventSharingFacts` | **#20 全族 30/30** | `postmerge-sharing30.log` |
| `dotnet build AnotherFSM.sln -c Release --nologo -p:NuGetAudit=false` | **0 错误，5 警告**（增量构建；完整警告见原日志，不据此宣称消除了历史警告） | `postmerge-release-build.log` |
| `bash tools/test-typed-context.sh` | 正例编译/执行 + **8 真实编译负例**；含生成消费者错误聚合 CS1503 | `postmerge-compiler.log`；`compiler/*.txt` |
| `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | 生成类、类型化 Begin/Launch 访问器真实编译、查询，Module 业务执行结果 42；原 Module/相对 imports/Namespace 回归保留 | `postmerge-generated.log` |
| `npm --script-shell='C:/Program Files/Git/bin/bash.exe' test --prefix demos/FSMDemo.Frontend` | **12/12** | `postmerge-frontend-test.log` |
| 同 npm 参数 `run typecheck`、`run lint` | 均退出 0 | `postmerge-frontend-{typecheck,lint}.log` |
| `RUNTIME_CONTRACT=true bash tools/build-browser.sh` | **Release .NET WASM publish + React 实际构建成功** | `postmerge-browser-build.log` |
| `PORT=5180 node tools/run-browser-tests.mjs` | 后端离线真实 Chrome **WASM 12/12**，随后真实 **API 同合同 7/7**；均 0 unexpected/0 skipped | `postmerge-browser-runner.log`、`postmerge-browser/{wasm,api}-results.json` |
| 读取 JSON stats + 尝试连接 5079/5180 | 报告计数核验；两个端口均关闭，runner 自己的服务已清理 | `postmerge-browser-summary.log` |
| `git merge implement/context-lifecycle` / `git diff --check` | 最新集成已包含；无待解冲突、无差异格式错误 | `handoff.txt`、`final-diff.log` |

最终浏览器使用真实 Release `_framework` 产物、系统 Chrome 与实际双 Adapter；不是 .NET session 替代。WASM 套件先于 API 启动，现有网络断言证明没有 REST/SignalR 回退。加载失败明确重试，未知节点/外部文件模块失败，Module 投影和编辑导出往返均在真实浏览器中执行。

### 逐 AC 对照（#21 正文顺序）

| AC | 生产接入与公开证据 |
| --- | --- |
| 1 同一绑定方案、显式脚本/聚合配对 | `TypedConsumerFacts.EveryRealAssemblyPathUsesTheSameTypedPlan`：直接 C#、Fluent、Fluent AddModule、真实 module/use DSL，均 BindContext/Prepare 并得到 42。`HostContext.Create<TInput>` 和两个生产 Program 显式选类型/实例；无新 DSL/类型猜测 |
| 2 真实 Module 唯一/显式默认/覆盖/全来源 | `RealModuleOverrides...`、`RealModuleLocalEntry...`：真实 namespace Area/module/use，两实例；DefaultSource、B.Process 覆盖 Other=100→101，默认对象仍 41；未配置多来源报歧义；局部入口仍拒绝 B.Process.Other null，并保留限定名/接口/来源；核心 TypedContextFacts 保留其余优先级/冲突反例 |
| 3 保留普通 Module/Namespace | 既有 Module 核心全族 + 两模式 browser `preserves Module interfaces...`：公开 Inputs、两个真实 Output 发布者、重复 Branch 按节点去重、不重绑、完整限定事件、实际归属及同前缀普通节点、编辑往返；未创建运行时代理/子引擎 |
| 4 三种实际工厂 | `ActualProductionFactoriesDeclareCreateBindAndExecuteBusinessNodes`：生产 AssemblyScanning、Browser 安全注册、真实 Autofac ContainerBuilder/ResolveKeyed；均核对声明类型与实际节点并执行结果 42，错误键保限定名/根因。`RealAutofacDelegateMetadata...` 在 BindContext 拒绝 IFSMNode 声明掩盖真实 IQueryBusiness；没有用假工厂代替这些路径 |
| 5 真实生成消费者 | `BusinessModules.FSMScript` AdditionalFile，生成 BusinessModules.Begin 为真实类型、Launch 为真实事件；显式绑定 DemoBusinessInput，实际编译/查询/执行；错误输入真实 CS1503；生成器未改、无绑定生成器 |
| 6 Demo/替身/Control/Trigger | Demo 五节点、生成测试节点全部从 Context.Token 迁至 Control.Token；新增局部业务节点只读 Context，核心 TypedExecutionFacts 验证 TriggerEvent/Control。FakeNode 明确 ContextType=null，仅旧接口形状暂存；无空业务对象/旧 Data 包装/调用者强转 |
| 7 C# 宿主负责输入、无前端依赖配置 | `ScanningHostExplicitlyPairsAggregateAndCreatesFreshInputPerExecution` 每轮 Job、结果 42/run 递增；`MissingNecessarySource...` 准备前缺 Approval 零节点；`InvalidCandidateBusinessAssembly...` 缺 IApprovalBusiness 来源在装配前拒绝并保留暂停原执行；浏览器同合同也验缺来源。只有固定结果 DTO，前端不传输入业务袋/CLR 名称 |
| 8 普通 start 立即拒绝、不变状态/绑定 | `DuplicateStartFailsWithoutChangingTheRunningExecution`、`PreparingHost...`、`SessionStartDoesNotQueue...`、`ExternallyPreparedEngine...`、`FinishedBeforeTrueExit...`；覆盖 pending 准备、外部 Prepare、运行/暂停、Finished 未退出。来源工厂不被第二 start 提前调用；停止并等待后每次新输入可再运行；API/WASM browser 共同断言暂停状态/业务 run/通知数不变 |
| 9 显式 Import/停止/资源屏障 | `ExplicitImportWaitsForActualNodeCleanupBeforeDisposalAndNewExecution` 取消后 finally 受控不退出，Import pending、节点未 Dispose、Completion 未完成、新 start 拒绝；释放后才 Import 成功/Dispose/新节点运行。无效候选保旧执行；共享 core 生命周期与 #20 族全保留 |
| 10 API/WASM 同 EditorRuntime 合同 | `C# host binds fresh business input, rejects conflicts and missing sources` 同一个 for-mode 合同：结果42/run1、重复 start 错误且原状态/通知不变、Stop 后 run2、Import 显式替换、缺失 Approval 零执行、重新导入 run3/旧通知隔离；两种实际模式均通过 |
| 11 编辑/Module/DTO/错误反馈 | 原编辑/导出/Module 投影合同及 editor 全流程保留，新增安全结果字段由两 Adapter 原有 DTO 通道读取；UI 失败不 resetExecution、错误可见且旧日志保留，Finished 仍可 Stop；旧 Group/Parallel 创建/导入拒绝测试不退回 |
| 12 真 Release 浏览器安全验证 | 合并后独立 publish/真实 Chrome WASM12+API7；实际 getter/接口/裁剪元数据、JSON/JSInterop、新宿主契约成功；浏览器白名单、外部文件模块、无远程 fallback/网络隔离沿用且真实执行；无 AOT/Worker 假要求 |
| 13 真实诊断 | `RealBrowserFactoryWrongKey...` 直接 C#/DSL 保 Area.Broken/NoSuchKey/真实根因及 InnerException；三工厂 WrongKey 均保 Actual.Bad；Autofac 元数据报真实声明/实际类型/接口；Module 来源错误保 Area.B.Process/Other/null。没有编造 DSL 行列 |
| 14 双语示例及暂存入口清单 | README 中英、consumer-host 中英、typed-context 中英、react-runtime/前端 README、业务 .fsm 更新；新业务路径不依赖 Data，过渡核心入口完整列在 consumer-host 的最后一节，交 #22 收缩 |
| 15 范围边界 | 无新 WPF/WebView2、依赖配置面板、共享/多引擎 UI、AOT/Worker/线程要求；#20 合入仅兼容交接，不重做通信能力，不继续 #22，不推送/关票/合主工作树 |

## 交付与限制 / Handoff and limitations

`evidence-21/raw/` 保存关键 red/green、所有最终各层日志、合并前失败浏览器 trace 和合并后 browser-results JSON；`evidence-21/compiler/` 保存真实编译器正负例；`handoff.txt` 记录最终分支/SHA/集成 tip，`SHA256SUMS.txt` 校验这些原件。不复制 node_modules/bin/obj/release 二进制。

以上为首次交付时的结论；后续可见独立审查发现的初始化取消阻塞及修复见下节，不能用首次全绿结果否定该缺陷。npm 既有 18 个 audit 风险与历史编译/裁剪警告未越票整治；业务自行脱离节点生命周期的任务、非协作同步代码永久不返回仍由原契约保持占用，不提供强制终止。最终收缩旧公共入口由 #22 承担。未自行组织独立审查。

## 可见审查 P1 修复：初始化期间先取消、后等待 / Startup retirement fix

### 基线、证据与责任

本次仅使用 `C:/Work/AnotherFSM-worktrees/review-21`，原分支不变；起点 HEAD `9db04b18ccd2500c02219d6606d8bcc040b10161`，已包含集成 `6f43cf9bcb793ba899713cf59e9b7f4d3e6fe14d`。未触碰主工作树、未委派/启动代理、未自行审查，也未推进 #22。

Standards 可见审查将此问题定为 **P1**；Spec 审查同根因标为 P2，没有新增独立问题（缺失/范围蔓延/推测均为 0）。主代理已亲自回读并复现：

- `review-21-standards-repro/StartupStop.cs`、`StartupStopTyped.cs`：无业务/类型化两路均无法在初始化释放前收到宿主取消。
- `main-21-retirement-repro/run.log`：Git Bash 可执行工程输出 plain/typed **False**，直接核心 **True**。
- `review-21-spec-repro/Program.cs`、`init-stop-core-comparison.log`：初始化明确协作等待取消，宿主失败而核心正常。
- 本次只读取 C# 与日志，没有运行原目录 `.ps1`。上述原始文件的副本及本次日志保存在 `C:/work/anotherfsm-context-19/evidence-21-fixes/`。

根因在 `EngineManager.RetireExecution`：先把 `_starting.Task` 并入退休屏障，而 `CleanupExecutorAsync` 在请求 `StopAsync` 前等待该屏障。初始化协作等待 `Control.Token` 时，形成“取消等启动，启动等取消”的循环。原来的运行期/准备期测试未覆盖这一交错。

### 最小生产处置

仅修改 `demos/FSMDemo.Runtime/EngineManager.cs`，不修改 #19/#20 核心算法、不引入通用生命周期框架或新公共 API：

1. 先在宿主锁内摘除旧执行器/通知并保留启动与退休屏障；原先的 `Task.Yield` 仍保证不在持有宿主锁的同步栈上进入核心/用户取消回调。
2. 锁外先调用旧执行器现有的 `Dispose()`，立即请求取消；该操作不 Dispose 节点、输入或来源，也不释放引擎占用。
3. 复用核心已有的 disposed 标记：Observe 已安装但 round 尚未发布时，后续 `RunPreparedContext` 仍会取消新发布的 round，避免一次普通 Stop 被空 round 忽略。
4. 然后等待原启动屏障，再 `await old.StopAsync()` 等真正退出，最后才允许旧图节点释放。尚未创建执行器的输入工厂/Prepare 阶段继续等待原启动屏障；普通 start 继续立即拒绝冲突，不恢复隐式抢占。
5. 无业务路径若退休恰好发生在第一次 RestartAsync 的 disposed 检查前，仅将“本宿主已退休且执行器尚未发布 round”的 ObjectDisposedException 转成本次未启动；初始化自身抛出的异常（包括同名 ObjectDisposedException）仍向调用方及 Completion 暴露。取消回调失败仍由核心 Completion/StopException 及退休日志观察。

### 真实 red → green 与同步点回归

接缝沿用用户已确认的公开 `EngineManager` / `RuntimeSession` / 节点控制入口，不使用反射、私有字段或内部 mock；超时只限制等待，不靠延时碰撞：

- 首个单行为 `StopCancelsInitializationBeforeWaitingForStartupAndTrueExit` 在修改生产代码前运行：`init-stop-red.log` **1 失败/0 通过**，在等待初始化取消处真实超时。初始化等待 Cancelled 后仍等待 Release，失败时 finally 只为清理释放测试节点。
- 同一测试最小修复后：`init-stop-green.log` **1/1 通过**。随后将该行为参数化，不把后加覆盖伪称为额外生产红灯。
- `tests/FSMDemoApiTest/StartupRetirementFacts.cs`：**21/21 通过**（`init-retirement-final.log`）。覆盖无业务/类型化 × Stop/Import/Dispose；取消后初始化仍未返回时，退休/Completion 保持 pending、占用不释放、节点与借用来源不 Dispose、普通 start 被拒绝、节点运行次数为 0。
- 同一测试族还覆盖 Observe 后、legacy 入口前以及 round 发布前的交错；使用公开节点 Context setter/需求入口与可控 SynchronizationContext 排程，先运行退休的首个 yield，再发布 round，证明取消不会丢失。输入工厂和 Prepare 来源 getter 两阶段分别覆盖三种退休入口；取消回调跨线程读取宿主状态不被宿主锁阻塞；初始化和取消异常仍可观察并等待真退出。
- `tests/FSMDemoWasmTest/StartupRetirementSessionFacts.cs`：**6/6 通过**（`init-session-green.log`），同一 JSON 协议验证两类节点的 Stop/Import/Dispose：取消在启动完成前送达，退休等待初始化退出，旧节点不运行、不产生 NodeActivated。
- 扩展测试初版曾误用桌面异步观察者完成通知证明 Dispose 已执行；该通知受既有 scheduler 释放影响，2 个用例超时（`init-retirement-expanded.log`，10 过/2 失败）。改用明确排程的同步点后 18/18，再增加 legacy 入口与同名异常覆盖得到最终 21/21；这属于测试接缝构造修正，不记为第二个生产缺陷或伪造 red→green。

### 修复后完整分层验收

均在本次唯一工作树执行；长命令由 Git Bash 后台运行到持久日志，没有后台代理或 sleep 轮询。所有 bash 调用显式 timeout 15–120 秒，npm 显式 Git Bash runner，JRE 沿用上文。日志相对 `evidence-21-fixes/`：

| 命令 | 修复后结果 | 原始日志 |
| --- | --- | --- |
| `dotnet test AnotherFSM.sln --nologo -p:NuGetAudit=false` | **核心 264 / API 77 / 宿主 .NET 36，全过，0 失败/0 跳过** | `solution-test.log` |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --no-build --no-restore --nologo --filter FullyQualifiedName~EventSharingFacts` | **#20 原族 30/30** | `sharing30.log` |
| `dotnet build AnotherFSM.sln -c Release --nologo -p:NuGetAudit=false` | **0 错误，5 个既有测试警告** | `release-build.log` |
| `bash tools/test-typed-context.sh` | 正例编译/执行、**8 个真实编译负例**均满足预期 | `compiler.log`、`compiler/*.txt` |
| `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | 生成消费者真实编译、类型化访问器查询、Module 业务执行成功 | `generated.log` |
| `npm --script-shell='C:/Program Files/Git/bin/bash.exe' test --prefix demos/FSMDemo.Frontend` | **12/12** | `frontend-test.log` |
| 同 npm 参数 `run typecheck` / `run lint` | 均退出 0 | `frontend-typecheck.log` / `frontend-lint.log` |
| `RUNTIME_CONTRACT=true bash tools/build-browser.sh` | **真实 Release .NET WASM publish + React 构建成功** | `browser-build.log` |
| `PORT=5180 node tools/run-browser-tests.mjs` | 真实 Chrome **WASM 12/12 + API 7/7**；两份 JSON **0 skipped / 0 unexpected / 0 flaky** | `browser-runner.log`、`browser/{wasm,api}-results.json` |
| 解析 JSON stats、连接检查 5079/5180 | 计数符合预期；**两端口均 closed**，仅 runner 自有服务已清理 | `browser-summary.log` |
| `git merge implement/context-lifecycle` / `git diff --check` | 最新本地集成 tip 仍为 `6f43cf9`，已包含、无冲突；格式检查通过 | `integration.log`、`handoff.txt` |

真实浏览器使用本次修复后重新 publish 的 Release 产物，不是修复前产物或 .NET session 替代。离线 WASM 先于 API 运行；同一 EditorRuntime 的业务绑定、重复启动拒绝、停止后新输入、显式替换、Module/编辑往返及无远程回退合同全部通过。`regression-progress.log` 和各 `.exit` 记录所有命令明确退出 0；没有把工具超时或缺失结果当成功。

**本次已知 P1 已修复，没有新增未解决的本票阻塞。** 残余限制仍为既有 npm audit、nullable/打包/裁剪及 Vite 警告；本次未做依赖升级或通用异常/观察者排空整治。非协作同步初始化永久不返回仍保持占用，不承诺强制终止。未 push、关票、合主树或继续 #22。最终提交/SHA、受测源文件校验与证据校验见同目录 `handoff.txt` / `SHA256SUMS.txt`。

### 主代理最终验证与正式可见复审

修复提交 `6758e79ec0ddc33b44292a6ee2fa2f2e0dd31b1f` 后，主代理重新构建运行 solution：**核心264/API77/宿主.NET36** 全通过；亲自重跑原 plain/typed/core 对照复现，现均在初始化释放前收到取消，状态 Stopping、Stop 仍等待退出、Execute 为 0。

主代理再次从修复源码发布真实 Release WASM，再运行 Chrome 合同：**WASM12/API7**，两份 JSON 均 **0 skipped/0 unexpected/0 flaky**；执行后 5079/5180 均 closed。原始日志与 JSON 独立留存于 `C:/work/anotherfsm-context-19/evidence-main-21-fixed/`。修复前主代理前端12/typecheck/lint、8真实编译负例、生成消费者及浏览器结果亦另行保留，不混作修复后的结果。

正式两轴均通过可见 Agent 工具使用 code-reviewer 类型，未把早期/其他阶段报告当最终结论。主代理已逐条阅读补丁/公开同步点回归、独立运行复现后采纳同根因问题并统一交由一个可见修复者处理：

- **Spec**：原 P2 闭合，补丁新增缺陷 **0**，推测 **0**；复审独立运行 API选集 **28/28**、宿主 **36/36**。
- **Standards**：原 P1 闭合，补丁新增实际缺陷 **0**，硬规范违规 **0**、smell **0**、推测 **0**；复审独立运行 API新增族 **21/21**、Session新增族 **6/6**。

两轴确认先离开宿主锁再请求取消、随后保留启动/真退出屏障，sticky disposed 覆盖发布轮次前交错，极窄异常过滤不吞已发布轮次的初始化错误。本票所有已知审查发现均已处置，无已知未满足的 #21 AC；仍不代表 #22 过渡入口删除或父规格最终验收已完成。
