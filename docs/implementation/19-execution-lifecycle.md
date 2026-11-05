# #19：执行生命周期与轮次隔离验证

## 交付范围

父规格 [#16](https://github.com/Naoki326/AnotherFSM/issues/16)，本票 [#19](https://github.com/Naoki326/AnotherFSM/issues/19)。基线 `07c2f08334dcc641117d2505a1ba3fea5d42f678`，首阶段提交 `48957a3227bb6c51986b024a03498d7620d67250`。

本票交付 Prepare 原子占用与显式放弃、节点实例归属、具体执行句柄、默认隔离与旧轮次隔离、真正退出屏障、取消异常与非协作节点、暂停队列、保留重启入口的安全保障及双语核心说明。共享集合/协调启动由 #20 承接，宿主普通启动及消费者迁移由 #21 承接，最终删除过渡入口与整体验收由 #22 承接。

## 主代理接管后的实际验证

以下为接管后对包含审查修复的工作树重新构建运行，不沿用阶段日志充当最终结果：

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo -p:NuGetAudit=false` | 216 通过，0 失败 |
| `dotnet test tests/FSMDemoApiTest/FSMDemoApiTest.csproj --nologo -p:NuGetAudit=false` | 39 通过，0 失败 |
| `dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --nologo -p:NuGetAudit=false` | 27 通过，0 失败；仅宿主 .NET |
| `bash tools/test-typed-context.sh` | 正例编译/执行及 7 个真实编译负例通过 |
| `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | 生成消费者编译、查询及真实 Module 执行通过 |
| `git diff --check` | 通过 |

工作树日志位于 `.scratch/main-19/`（本地忽略目录）。此阶段没有运行真实 Release WASM 浏览器，不能用上述宿主 .NET 或编译结果替代。

## 原阶段发现的逐条处置

原阶段审查产生于持续变化的中间快照，不直接视为最终独立双轴审查。主代理已阅读生产代码和对应公开接缝回归，并重新运行上述测试：

| 发现 | 处置与回归 |
| --- | --- |
| 旧 `Handle` 回调可驱动新轮次 | 采纳。无轮次身份入口明确拒绝，具体句柄/捕获路由进入私有接收路径；`UnscopedHandleCallbacksCannotDeliverIntoALaterExecution` 覆盖普通及结束事件 |
| 替代入口后继可借用外引擎节点 | 采纳。完整可达图在改写业务/控制前检查身份归属；`EveryRestartRejectsForeignDescendantsAddedAfterConstructionBeforeRebinding` 覆盖四个重载 |
| 拦截事件时 Pause 后仍提交转移 | 采纳。拦截后选目标及实际调用节点前复核暂停；`PauseDuringInterceptionPreservesTheTransitionUntilContinue` 用公开调度/拦截屏障验证 |
| 继续标记残留到新轮次 | 采纳。统一新轮次初始化清除恢复标记和信号；`ResumeAcceptedAtNodeExitCannotLeakIntoAnyRestartOverload` 覆盖四个重载 |
| 核心控制与宿主通知锁反序 | 采纳。退休操作不在宿主状态锁内调用核心，宿主节点外部通知不持状态锁；`HostContinueDoesNotHold...`、`HostNodeCallbackCanContinueWhileAnotherThreadFinishesTheExecution` 回归 |
| 相同 Prepare 的第二次 Start 等待用户初始化 | 采纳。锁内只标记已启动，用户初始化在锁外；`DuplicatePreparationStartRejectsWithoutWaitingForUserInitialization` 回归 |
| 双语说明仍称 Prepare 不占用 | 采纳。`docs/typed-context.md` 和英文版说明占用、释放、具体句柄、完成屏障和旧入口拒绝；README 同步标注后续票边界 |

## 验收族与测试导航

- 原子 Prepare、准备竞争、占用覆盖停止/Finished 未退出：`ExecutionLifecycleFacts` 的 PreparedExecution、ConflictingPrepare、ConcurrentPrepare、FinishedAndWaitCurrentTask 等场景。
- 默认隔离/真实并发/轮次：同类中的 ConcurrentEngines、SameEventIdentity、LateNodeNotification、QueuedBusinessEvents 等场景；`ExecutionReviewFacts` 补旧公共入口反例。
- 退出与所有权：CancellationCallbackFailure、DisposeAndCallerTimeout、StopCompletionWaitsForAsyncIteratorCleanup（同步/异步清理）及四类 EveryRetainedRestart 场景。
- 暂停/触发/私有状态：PausedBusinessEnd、PausedOrdinaryAndEndEvents、BusinessEndConcurrentWithContinue、TypedExecutionFacts 和 ExecutionReviewFacts。
- 受影响宿主/Module：RuntimeSessionFacts、EngineWiringFacts、Module 测试族及 API 图编辑回归。
- 类型约束及固定业务绑定：TypedContextFacts、TypedContextCompile 和编译负例脚本；完整消费者迁移不在本票声称完成。

最终独立 Standards/Spec 复审及其处置见下节；本修复不宣称 #19 或父规格已整体验收。

## 可见 Spec / Standards P1 修复（本轮）

工作目录固定为 `C:/Work/AnotherFSM-worktrees/ticket-19-execution-lifecycle`，分支 `ticket/19-execution-lifecycle`，接手 HEAD `249b85d155529d00c8a1d32503c7a1e69ef49f9d`。已执行 `git merge implement/context-lifecycle`，集成 tip 为 `07c2f08334dcc641117d2505a1ba3fea5d42f678`，结果 Already up to date。本轮未调用代理、未另做独立审查、未改主 worktree；不涉及 #20/#21/#22。

| 已采纳发现 | 修复与公开接缝回归 |
| --- | --- |
| Spec P1：用户 `InitBeforeStart` 阻塞时尚无本轮完成任务，StopAsync 提前成功，Stop/Dispose 被后续启动重置 | `StartOwned` 在同步用户初始化前发布本轮 `FSMExecution` / `ExecutorTask` 并初始化控制字段；初始化在 control/engine 锁外，返回后不重置停止状态。类型化 Start 与四个旧 Restart 共用此入口。`StopDuringEveryRestartInitializationWaitsAndPreventsNodeExecution` 覆盖四个重载 × Stop/Dispose：初始化未返回时停止不完成、占用不释放；放行后零节点执行，随后可重新 Prepare。 |
| 初始化异常必须保持失败、不得放行节点或提前释放清理占用 | 初始化异常走原有 `RunOwnedAsync` 清理/退出路径并保持启动错误可观察；准备包装层不再抢先释放已发布执行的占用。`InitializationFailureNeverRunsNodesAndReleasesOnlyTheFailedRound` 覆盖类型化 Start 和四个 Restart：初始化屏障内竞争 Prepare 立即失败，失败后零节点执行、旧完成任务故障、随后可准备并启动新轮。原有 `DuplicatePreparationStartRejectsWithoutWaitingForUserInitialization` 保持通过。 |
| Standards P1：同步取消回调 inline 驱动节点退出，使 Completion/占用释放早于回调返回和 StopException 捕获 | Stop 在通知/取消前登记本轮取消请求屏障，取消仍在原调用线程同步执行，但不持请求自身的 control/engine 锁；完成与释放在锁外异步等候回调返回，最终提交前再次核对并发 Stop。具体句柄 Stop/StopAsync 原子验证身份并捕获对应完成任务；Dispose 不再额外持锁调用取消。`CancellationCallbackMustReturnAndReportErrorsBeforeCompletionOrEngineReuse` 故意用允许 inline 延续的任务释放节点，随后将取消回调停在屏障：Completion 未完成，竞争 Prepare 立即拒绝且绑定不变，并发 Continue 不阻塞且返回 false；回调抛错时 Completion 必须故障、StopException 可观察。 |

### 本轮真实 red → green 与最终验证

- 第一行为先运行新增初始化回归：8 失败，均因初始化屏障内 StopAsync 已完成（`.scratch/19-init-red.log`）；最小修复后新增 8 例及既有重复 Start 共 9 通过（`19-init-green.log`）。
- 第二行为先运行新增 inline 取消回归：2 失败，均因回调仍在屏障内 Completion 已完成（`19-cancel-red.log`）；修复后当时的 `ExecutionReviewFacts` 26 例全通过（`19-cancel-green.log`）。随后补充 5 个初始化失败/顺序复用回归，进入最终全量。
- 测试只通过公开装配、准备、启动、执行句柄及控制接口观察；使用明确同步点。超时只限制等待，不以 sleep 推断时序。生产初始化没有改为 Task.Run，取消也没有转移线程；保留现有浏览器协作调度。

| 最终命令 | 本轮结果 / 本地日志 |
| --- | --- |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo -p:NuGetAudit=false` | **231 通过，0 失败**；`.scratch/19-core-final.log` |
| `dotnet test tests/FSMDemoApiTest/FSMDemoApiTest.csproj --nologo -p:NuGetAudit=false` | **39 通过，0 失败**；`.scratch/19-api-final-rerun.log` |
| `dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --nologo -p:NuGetAudit=false` | **27 通过，0 失败**，仅宿主 .NET；`.scratch/19-host-final.log` |
| `bash tools/test-typed-context.sh` | 正例编译/执行及 **7 编译负例通过**；`.scratch/19-compile-final.log`，详细编译输出在脚本既有 `.scratch/ticket-18/compiler/` |
| `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | **生成消费者编译、查询及真实 Module 执行通过**；`.scratch/19-consumer-final.log` |
| `dotnet build AnotherFSM.sln -c Release --nologo -p:NuGetAudit=false` | **0 错误、61 警告**；`.scratch/19-release-final.log` |
| `git diff --check` | 通过；仅 Git 的既有 LF/CRLF 转换提示 |

API 首次与宿主并行构建时竞争同一 analyzer nupkg，出现文件占用错误（`.scratch/19-api-final.log`），已改串行重跑通过，不隐去该失败。生成消费者及 Release 构建使用 `JAVA_HOME=C:/Users/Naoki/.jre/jdk-11.0.26+4-jre`。日志为本地忽略产物。

剩余验证边界：本轮没有真实 Release WASM 浏览器运行；宿主 .NET 测试/Release 编译不代替它。同步用户初始化/取消回调本身若不返回，仍保持本轮占用并等待，框架不强行终止用户代码。

## 主代理最终核验与可见双轴复审

修复提交：`8e24d1c98cd3b649f479fbca116446f52732af05`。主代理在该提交重新构建运行核心 **231**、API **39**、宿主 .NET **27**，全部通过；日志 `.scratch/main-19/{core,api,host}-fixed.log`。

本次正式双轴审查均通过可见 `Agent` 工具使用 `code-reviewer` 类型执行，明确禁止再次委派或启动 CLI 代理。首次发现的两个 P1 均由主代理阅读代码/公开复现并亲自运行后采纳，统一交由一个可见实施者修复；没有将早期隐藏 CLI 审查当作最终验收。

| 轴 | 首次审查 | 修复后独立复审 |
| --- | --- | --- |
| Spec | 初始化期间 Stop/Dispose 提前宣称退出，1 个 P1 | 原 P1 闭合，补丁新增实际缺陷 **0**，推测 **0**；独立运行 ExecutionReviewFacts **31/31** |
| Standards | 文档硬违规 0、基线 smell 0；另发现取消回调 inline 提前完成/释放，1 个 P1 | 原 P1 闭合，补丁新增实际缺陷 **0**，推测 **0**；独立运行 ExecutionReviewFacts **31/31** |

主代理已逐条对照补丁复核：初始化前发布本轮完成任务，初始化在锁外且返回后不清除停止；初始化异常走本轮清理；取消前登记屏障，回调/错误采集完成后再提交 Completion 与释放；具体句柄停止及 Dispose 不额外持控制锁执行取消。所有本票已知发现均有处置，没有已知未满足的 #19 AC；这不代表 #20–#22 或父规格已完成。
