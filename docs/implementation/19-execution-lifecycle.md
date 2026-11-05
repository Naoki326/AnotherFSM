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

最终独立 Standards/Spec 复审及其处置另行补记；在完成前不宣称 #19 或父规格已验收。
