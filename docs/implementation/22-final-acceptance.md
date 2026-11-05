# #22 最终收缩与父规格追溯

实施基线：`63308a7486b73a3ed4cd495689e225a14498923d`；HEAD / merge-base / `implement/context-lifecycle` 三者一致，隔离 worktree 开工干净。#17–#21 已交付关闭；#22 已认领、原生依赖 #20/#21 closed（外置 `claim.json` / `dependencies.json` / `closed.json`）。本票不 push、不关票、不合主树、不委派或自行审查。

原始日志：`C:/work/anotherfsm-context-19/evidence-22/`。本文件为版本化交付入口，不以 ignored 目录保留唯一报告。

## 逐 slice 的真实红绿

1. 无依赖准备：`01-free-red.log` 为缺少 `FSMEngine.Prepare` 的真实 CS1061；首次同时含 fixture 把 void CreateNode 当返回值的 CS0030，已修正。实现无业务输入准备、全引擎需求检查与原子占用后 `01-free-green.log` 1/1，补充必要来源不得免绑定后 `01-free-contract.log` 2/2。没有空业务对象、动态来源或另一套启动算法，复用 #19/#20 的 preparation。
2. 旧入口删除：`02-removal-red.log` 真实证明旧构造仍可编译；删除公开构造、四个 Restart 和无轮次 Handle，正常宿主及生成消费者统一 Prepare。`02-entry-compiler.log` 的前八项分别实际检查 CS1729 / CS1061；随后旧 Context 尚可编译，留作下一 slice 的真实红灯。`02-core-adaptation.log` 记录迁移后的 9 个 fixture/断言适配失败，没有删除安全测试换绿；校正真实元数据、显式引擎图归属、初始化停止同步点后 `02-solution-green.log` 核心266/API76/宿主.NET36 全过。

原四个重启入口通过四个真实编译负例拒绝；原安全族迁到新公开路径：同引擎竞争、真退出前重绑拒绝/新输入、四种 Start 形状的初始化停止与异常、引用身份、私有初始化、恢复标记、旧句柄/迟到回调、旧队列及外引擎实例归属。API 的单个“Observe 后第一次 legacy Restart 前”专属场景随该入口删除，保留无依赖/类型化的 Observe→round 发布窗口与初始化取消屏障，并由编译负例证明旧入口不可复活（API 77→76）。

3. 包装/传播收缩：以上 `OLD_CONTEXT unexpectedly compiled` 是本 slice 的真实红灯。删除 FSMNodeContext 两种形态、非泛型 Context 与沿转移整包覆盖，IFSMNode 控制/触发独立；AsyncEnum/Yield 不再依赖旧包装。保留只读泛型局部 Context。`03-wrapper-contract.log` 核心267/API76/宿主.NET36 全过；`03-removal-compiler-green.log` 的 12 项全部得到实际预期 CS1729/CS1061/CS0246。`03-positive-execution.log` 真实编译并执行类型化与无输入准备正例。引擎注入旧名称职责保留；节点作用域 engine.PublishEvent 不作为外部入口，新增公开回归验证准备期、运行期与新轮的外部调用明确拒绝。

4. 组合证据加固（首次即绿，不伪称新增生产红灯）：真实 DSL 两个 Module 实例在 Prepare 后类内换来源、固定来源成员更新、共享局部视图与限定节点覆盖，然后显式输入下一轮；`04-module-fixed.log` 1/1。真实生成消费者执行同一组合，保留访问器/普通 Module/相对 imports/Namespace，`04-generated-fixed.log` 成功。
5. 首次最终 solution 真实失败保留于 `first-final-attempt/`：核心266过/1失败，API77/宿主.NET36过。隔离测试的 Entered 发生于节点同步前缀，未保证执行器已公告 Running，读到 Initialized。只补两个公开 Running 状态屏障，保留所有隔离断言，不改生产语义；`05-state-barrier-green.log` 该行为 1/1。

## 父规格验收族 → 实施票 → 最终测试

下表按 [#16 完整验收族](https://github.com/Naoki326/AnotherFSM/issues/16)（及 #13 最终迁移矩阵）追溯；故事编号覆盖全部 58 条。测试名是当前生产源码的真实接缝，不将历史原型或静态查找算作执行通过。最终命令结果另列于后节。

| 父验收族 / 用户故事 | 交付票 | 当前实际证据入口与关键测试 |
| --- | --- | --- |
| 异构 Context 与来源选择 / 1–9、18 | #18、#21；#22 无输入准备与收缩 | 核心 `TypedContextFacts.LocalInterfacesShareTheAggregateMemberThroughRealExecution`、`QualifiedNodeExceptionWinsOverTheSharedDefaultAndSurvivesTransition`、`ExplicitInterfaceDefaultOverridesUniqueCompatibleAutomaticSource`；`FinalContractFacts.DependencyFreePreparationNeedsNoBusinessInstanceAndStillOwnsTheEngine`；API 项目 `TypedConsumerFacts.EveryRealAssemblyPathUsesTheSameTypedPlan` 四路径真实执行 |
| 静态约束与分阶段诊断 / 10–13、15–16、20 | #18、#21、#22 | `tools/test-typed-context.sh` 的来源兼容/错误聚合/init/private-set/readonly/Context 重绑/内部绑定入口/生成消费者错误聚合真实诊断；核心 `InvalidRelationshipsAreRejectedBeforeAnExecutablePlanExists`、`IllegalSourceExpressionsHaveAssemblyDiagnosticsWithNodeAndRequirement`、`OrdinaryPublicSetIsRejectedAtAssemblyButStableDeclarationsAreAccepted`、`AutomaticMatchingUsesOnlyDirectPublicInstanceReadableNonIndexedPropertyDeclarations`、`NullAndDynamicWrongInputsFailWithIdentityAndRootCauseBeforeAnyNodeRuns`、`ValidBusinessObjectsWithUnreadyFieldsAreHandledByBusinessNodesNotTheBinder`；三生产工厂见 TypedConsumerFacts。PrepareDynamic 只是显式类型检查入口，错误拒绝，没有 object/dynamic 兼容回退 |
| 绑定完整性与固定引用 / 14、19、25 | #18、#19、#21；#22 组合 | 核心 `UnreachableNodeSourcesAreStillRequiredWhenStartingAtALocalEntry`、`PreparationFixesIdentityBeforeFirstNodeAndSharesLiveMemberUpdates`、`HiddenSameNameSourceDeclarationsRemainIndependentThroughoutExecution`、`NewExecutionUsesTheNewExplicitAggregateRatherThanResidualBusinessBindings`；真实 Module 的 `RealModuleLocalEntryStillRequiresTheFullPlanAndReportsItsQualifiedSource`、`PreparedRealModulesKeepSharedViewsAndOverridesFixedThenUseExplicitNextInput`；生成消费者 `FixedReferencesAndOverridesInGeneratedModules` |
| 数据、控制与所有权 / 17、21–24、26 | #18、#19、#22 | `TypedExecutionFacts` 三个真实业务/无依赖/Yield 测试：`LoopReentryKeepsBusinessMutationsAndFrameworkDoesNotDisposeCallerResources`、`YieldPauseContinuePreservesBusinessAndTriggerWhileNewExecutionInitializesPrivateState`、`NonGenericFamiliesDeclareNoDependencyAndYieldControlsDoNotNeedAnEmptyBusinessObject`；生命周期的 `PreparationInitializesNodePrivateStateOncePerNewExecution`、`EveryPreparationEntryRejectsRebindingUntilTheOldExecutionTrulyExits`（4）；调用方对象不清空/复制/回滚/Dispose |
| 引擎/节点归属及退出 / 27–32 | #19；#22 新入口回归 | `ExecutionLifecycleFacts` 的 ConcurrentPrepare/ConflictingPrepare、`CompetingPreparationsOnlyLetOneExecutionBorrowTheEngine`、`NodeInstanceCannotBeAssembledIntoTwoEngines`、`PreparedGraphsCheckSharedNodesByReferenceNotBusinessEquality`、`PreparedExecutionOwnsEngineUntilExplicitlyReleased`、`FinishedAndWaitCurrentTaskDoNotReleaseOwnershipBeforeTheNodeCallbackReturns`、`DisposeAndCallerTimeoutAreNotExitAndNeverDisposeBorrowedResources`、`CancellationCallbackFailureRemainsObservableButDoesNotReleaseANonCooperativeNode`、`StopCompletionWaitsForAsyncIteratorCleanupAndKeepsEngineOccupied`；ExecutionReview 的初始化/inline取消两族保留。旧四重启改为四项编译拒绝，而非保留兼容层 |
| 默认隔离与共享集合 / 33–41 | #19、#20 | `ConcurrentEnginesWithTheSameEventIdentityDoNotFinishEachOther`（共享/clone/同名）、`SameEventIdentityDoesNotAuthorizeBusinessTransitionsInAnUnconnectedEngine`；EventSharing 的 `OverlapIsIdempotentBidirectionalAndSurvivesAnIntermediateExitWithoutSharingContextOrControl`、`FailedConnectCannotMergeRangesAndClonedEventIdsDoNotGrantMembership`、`AnActiveBusinessResponseRepublishesTheSameNameAsANewNotification`、`RealModuleOutputsKeepTheirFullCaseSensitiveLocalIdentityForOrdinaryAndEndEvents`（8）、`CoordinatedStartReusesFixedSourcesAndSharedPayloadsNeverReplaceTargetBindingsOrCanonicalEvents` |
| 启动与运行中加入 / 42–44 | #20；#21 观察者接入 | `AllReceiversAreReadyBeforeInitialNodesPublishAcrossIndependentLocalEventIds`、`InitialReceiverBarrierReleasesZeroNodesAndFailedPreparationsCanBeExplicitlyAbandoned`、`ReceiverPreparationFailureReleasesAllTemporaryResourcesAndRetainsRetryableOwnership`、`StartRejectsInvalidOrIncompleteSelectionsWithoutConsumingValidPreparations`、`RunningRangeKeepsWorkingDuringFailedJoinAndNeverReplaysPreJoinNotifications`；核心 ConsumerPreparationFacts 的 `HostObserverFailureKeepsPreparationRetryableAtTheSharedReceiverBarrier`、`HostMultiEndStartCannotBypassTheCompletePendingReceiverSet`。真实早到 end 回归 `ARealModuleEndReceivedBeforeTheInitialRunFinishesWithoutExecutingAndReleasesOwnership` 原样保留，不强迫 Finished 目标跑初始业务 |
| 暂停、停止与旧轮次 / 45–46 | #19、#20、#22 | `PausedOrdinaryAndEndEventsKeepOrderAndPayloadUntilContinue`、`ContinueAndLateEndCannotOverrideStopping`、`LateNodeNotificationKeepsItsOriginalRoundAcrossFreshPreparation`、`ConcreteHandleCannotOperateTheNextPreparedExecution`、`QueuedBusinessEventsAreNotCarriedAcrossAStoppedPreparedRound`；共享 `PausedMembersQueueOrdinaryAndBusinessEndEventsWithoutControllingOtherMembers`、`RestartedMembersNeedExplicitConnectionAndOldNodeRoutesCannotPublishIntoTheNewRound`、`ANotificationAlreadyInFlightCannotRetargetAReplacementThatJoinsBeforeDeliveryReturns`、`SharedEventsQueuedInAPausedOldRoundCannotSurviveStopAndExplicitReconnection` |
| Module 与生成消费者 / 47、55 | #17、#21；#22 真实组合 | TypedConsumerFacts 三生产工厂及四装配路径；Module、ModuleExport 全族；`EngineWiringFacts.OrdinaryBackendNodesInFlattenedModulesReceiveTheirEngineAndReallyExecute`（DSL/Fluent）；FSMScriptAnalyzerTest 真实 AdditionalFiles、类型化 Begin/Launch 访问器、相对 imports/Namespace、真实 Module 执行与固定共享/覆盖/下一轮组合。真实浏览器 `preserves Module interfaces, actual ownership and script round trips` 验证多 Input/Output、发布者去重、Branch 不重绑、限定事件和实际归属 |
| 旧组合退场 / 48–52 | #17；#22 最终复核 | `tools/test-removed-composition.sh` 六种公共形态与3专属入口共9编译负例；`RemovedCompositionFacts`（真实 DSL/模板/Transform/Fluent 与迁移诊断）；API `EngineEditingFacts`、宿主 `LegacyImportsFailBeforeExecutionAndPreserveTheExistingGraph` / `RemovedCompositeOperationReportsMigrationWithoutChangingTheGraph` / `LegacyCreationRequestsFailWithMigrationInsteadOfIgnoringConfiguration`；两种真实浏览器 `rejects removed composition without replacing the Module graph` 循环拒绝全部6键/模板/子流程描述/旧DTO/旧运输操作，并继续执行普通 Module。React 实际 editor 测试确认面板无 Group/Parallel，前端单测维持投影边界 |
| API / 真实 WASM / 53–54、57 | #21；#22 最终源码重发重跑 | `TypedHostFacts` 的成功/缺来源/外部Prepare/准备中冲突/无效候选保旧执行；`StartupRetirementFacts` 与宿主 StartupRetirementSessionFacts 控制初始化取消/真退出/锁外取消；`RuntimeSessionFacts.ExplicitImportWaitsForActualNodeCleanupBeforeDisposalAndNewExecution` 用节点清理屏障证明 Import 等待及资源所有权。真实 Chrome 同 EditorRuntime 的 `C# host binds fresh business input, rejects conflicts and missing sources` / `execution remains responsive and isolates notifications across stop and import` / Module往返/旧操作拒绝；WASM 独立 loading失败重试与零 API/SignalR 网络请求。受控非协作清理/初始化交错证据明确是核心/宿主.NET，不冒充浏览器探针 |
| 文档与仓库闭合 / 56、58 | #17–#22 | 中英 README、typed-context、consumer-host、event-sharing、React 指南、生成器 INTEGRATION 与生产示例全部指向最终契约；无输入图真实走 Prepare。issue/原生依赖 JSON、各票提交及验收记录证明组织交付，属于静态/工单证据，不当测试计数。完整 solution/Release 构建确认消费者可构建；含 ReadKey 的 FSMNodeAnalyzerTest 仅构建，不算无人值守执行门禁 |

### 前票交付权威

- #17：集成 `150013267c14d06b3a8265fe189683d97524776a`；[#17](https://github.com/Naoki326/AnotherFSM/issues/17) 交付及 [原审查记录](https://github.com/Naoki326/AnotherFSM/blob/150013267c14d06b3a8265fe189683d97524776a/.scratch/ticket-17/review.md)。
- #18：集成 `0d80339f91b1063649d5880980bd04374ec1d097`；[#18](https://github.com/Naoki326/AnotherFSM/issues/18) 交付及 [原审查记录](https://github.com/Naoki326/AnotherFSM/blob/0d80339f91b1063649d5880980bd04374ec1d097/.scratch/ticket-18/review.md)。完整来源声明链身份修复及8条公开回归保持，不退回名称缓存。
- #19：[版本化交付](19-execution-lifecycle.md)，正式修复 `8e24d1c98cd3b649f479fbca116446f52732af05`；初始化前发布 Completion、取消回调自身屏障及防 inline 提前释放保留。
- #20：[版本化交付](20-event-sharing.md)，正式修复 `73f176e304cf0a0885fe064a12879bcc4823f90a`；接收屏障/集合语义与早到 end 规范正例保留。
- #21：[版本化交付](21-consumer-host-migration.md)，正式修复 `6758e79ec0ddc33b44292a6ee2fa2f2e0dd31b1f`；宿主锁外先取消再等 starting/真退出保留。最终基线 `63308a7` 已含全部。

以上历史记录只证明前票各自交付，不替代本票源码的最终测试；本次没有启动独立审查流程。主代理负责最终整分支双轴审查与后续调度。

## #22 全部 AC 对照（正文顺序）

| AC | 最终交付与验证 |
| --- | --- |
| 1 全调用方迁移 | Core 泛型/非泛型节点和 AsyncEnum/Yield 使用独立 Control；IFSMNode 不再有业务包装。C#/Fluent/真实DSL/Module、三生产工厂、生成消费者、Demo、API/Browser、测试替身均走目标准备/执行。生产无依赖宿主与生成图直接 engine.Prepare，不造空输入。证据为真实构建、核心执行、TypedConsumerFacts、生成进程及浏览器，而非关键词清零 |
| 2 删除过渡契约/入口/桥接 | FSMNodeContext 两种形态、Data/As、非泛型 Context、IFSMNode.Context、上游整包传播、公开执行构造、四个 Restart、无轮次 Handle/IHandle 实现删除。内部唯一构造只由 preparation 创建，保留 FSMExecutor 的观察/控制职责。节点私有 Restart 是 Enum/Yield 初始化实现，不是公开执行入口；ReinitGroupNode 注入职责、engine.PublishEvent 节点作用域按实际功能保留，外部引擎发布明确拒绝 |
| 3 真实静态正负 | 8 个目标静态负例 + 12 个最终删除负例 + 9 个旧组合负例；每个检查真实 C# 诊断，原始 compiler 日志外置。正例真实执行类型化与无输入准备，生成消费者错误聚合实际 CS1503，最后恢复正例编译 |
| 4 真实组合 | 既有四种装配与三工厂真实执行；新增 DSL 双实例与真实生成 Module 的固定引用/成员更新/异构共享/限定覆盖/显式新输入组合；完整方案、Module Input/Output/发布者去重/Branch/归属回归保留 |
| 5 执行与共享组合 | #19/#20 完整公开族及新路径安全迁移；独立引擎并发、占用冲突、未启动释放、旧句柄/迟到/旧队列、暂停普通/end、初始化停止/取消异常/非协作、集合合并/退出/加入、全接收屏障/失败保准备，均对应父表测试；未重做通信、父子执行或新状态 |
| 6 API/ReleaseWASM合同 | 最终共享 Runtime 同 EditorRuntime，业务成功42、缺Approval零执行、重复start保旧状态/输入、Stop后新输入、显式Import与通知隔离、编辑导出/Module图及浏览器网络边界。真退出受控交错另由核心/API宿主.NET证明，层级不混称 |
| 7 旧组合退场 | 六公共形态真实编译失败；真实DSL/模板/Transform/Fluent/导入/旧运输请求明确拒绝；浏览器实际旧操作/UI/DTO/导出回归，没有自动转换、静默串行或内置并行替代 |
| 8 证据分层 | 下表分别列编译、核心、API、宿主.NET、真实Chrome ReleaseWASM/API、前端与静态。并发用公开任务/状态/初始化/清理屏障，超时只限制等待；完整保留首次失败，不用重跑绿掩盖 |
| 9 中英最终指南 | README、typed-context、consumer-host、event-sharing 中英，生成器INTEGRATION、React与前端说明改为最终API；Group→Module非等价、独立引擎共享责任及调用方资源所有权明确；历史票/原型只作历史，不推荐旧入口 |
| 10 父族追溯/真实状态 | 上表覆盖父12验收族及全部58故事，引用#17–#21正式交付/审查修复；最终检查未发现前票未交付能力或生产缺陷。首次最终回归的测试同步假设有具体失败/最小屏障修正与日志，不隐藏。本次不代替主代理整分支双轴审查 |
| 11 范围/隔离 | 只改工具提供的唯一 worktree/本分支；原始日志外置。不改状态拼写、全历史API、异常体系、依赖版本/lock、AOT/Worker/WPF/多线程平台能力；不触用户主树、不推送/关票/合主树、不清他人进程或worktree |

## 最终分层命令结果

受测实现为提交 `36625a7`（其后只更新验收文档）。最终回归在同一隔离 worktree 以 Git Bash 后台测试脚本执行，各命令独立日志及 `.exit` 明确保存；不是后台代理。JAVA_HOME 为 `C:/Users/Naoki/.jre/jdk-11.0.26+4-jre`，Windows npm 显式 Git Bash runner。第一次读取证据校验时浏览器尚未结束，校验因 runner.exit 不存在明确失败，该记录不会算测试成功。

| 层级 / 真实命令 | 本次结果 | 外置原始日志 |
| --- | --- | --- |
| 完整 solution Debug：`dotnet test AnotherFSM.sln --nologo -p:NuGetAudit=false` | 核心 **267** / API **77** / 宿主.NET **36** 全过，0失败/跳过 | `final-solution-test.log` |
| 完整 Release：`dotnet build AnotherFSM.sln -c Release --no-incremental --nologo -p:NuGetAudit=false` | **0错误、65警告**，非只靠增量构建 | `final-release-build.log` |
| Release 测试：`dotnet test AnotherFSM.sln -c Release --no-build --no-restore --nologo` | 核心 **267** / API **77** / 宿主.NET **36** 全过，0失败/跳过 | `final-release-test.log` |
| 类型化真实编译：`bash tools/test-typed-context.sh` | 正例编译/真实执行与 **8** 负例通过 | `final-typed-compiler.log`、`typed-compiler/*` |
| 最终删除真实编译：`EVIDENCE_DIR=... bash tools/test-final-contract.sh` | 正例与 **12** 负例通过，逐个 CS1729/CS1061/CS0246 | `final-removed-compiler.log`、`final-removed-compiler/*` |
| 旧组合真实编译：`bash tools/test-removed-composition.sh` | 正例及 **9** 负例通过，含6形态 | `final-composition-compiler.log`、`composition-compiler/*` |
| 真实生成消费者：`dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | 实际生成类编译、类型化查询、普通Module/Namespace/imports及固定共享/覆盖/新轮业务组合执行成功 | `final-generated.log` |
| 原#20完整共享族：`dotnet test tests/StateMachineTest/StateMachineTest.csproj --no-build --no-restore --nologo --filter FullyQualifiedName~EventSharingFacts` | **30/30** | `final-sharing.log` |
| 前端：`npm --script-shell='C:/Program Files/Git/bin/bash.exe' ci --include=dev --prefix demos/FSMDemo.Frontend`，同参数 `test` / `run typecheck` / `run lint` | 安装退出0；测试 **12/12**、typecheck/lint退出0 | `final-npm-ci.log`、`final-frontend-{test,typecheck,lint}.log` |
| Release浏览器构建：`RUNTIME_CONTRACT=true bash tools/build-browser.sh` | 实际 .NET Release WASM publish、反射裁剪、React生产构建成功，退出0 | `final-browser-build.log` |
| 真实浏览器：`PORT=5180 node tools/run-browser-tests.mjs` | 真实Chrome **WASM12/12、API7/7**，两份JSON均0 skipped / unexpected / flaky，runner退出0 | `final-browser-runner.log`、`browser/{wasm,api}-results.json`及原始日志 |

父表第1–8族的具名核心测试包含在本次核心267及Release267；涉及生产工厂/真实Module的具名API项目测试包含在API77及Release77；第9族另有实际生成进程成功，第10族另有9项组合编译负例，第11族的受控宿主测试包含在宿主36/API77。真实浏览器结果必须按独立行核验，不用.NET结果代替。

### 已知警告与范围

- 本次完整Release65警告，包括既有nullable、未使用成员、打包与确定性同步点的xUnit1031；不宣称仓库无警告。
- npm ci实际报告 **18 vulnerabilities（11 moderate、7 high）**；未变更依赖或lock，未擅自audit fix/升级。NuGetAudit按既定命令关闭，不据此宣称没有.NET依赖安全风险。
- Release publish保留反射/Reactive/Autofac裁剪警告、缺原生wasm-tools的提示；React构建有SignalR PURE注释与大chunk警告。不新增AOT/Worker要求；WASM兼容必须以真实Chrome结果为证，不从构建推断。
- 非协作初始化、取消回调或节点永久不返回仍占用并等待；调用方资源所有权不变。没有强行终止、可靠消息或自动回收业务游离任务承诺。

## 最终状态与交接

- #17–#21 已正式交付关闭；本次最终源码全部 #22 AC 无已知未满足项，父规格12验收族/58故事均有上表追溯。没有发现需作为前票未交付能力或生产缺陷回报的新增问题。已知测试同步失败/适配失败及修正全部保留，不隐去。
- `verify-results.py` 实际读取全部15个命令/总套件退出文件、Debug及Release三层计数、两份真实浏览器JSON，并连接核验 **5079/5180均closed**；最终退出0，原始 `final-results-verified.log`。没有把工具超时、No result或缺失输出当成功，也没有杀用户进程。
- 父表第9–11族的真实浏览器依据为本次重新Release发布后的WASM12/API7，非历史#21产物；WASM先于API启动且逐例监测零REST/SignalR回退。受控真退出/取消非协作交错是单独明确标记的核心/API宿主.NET证据，不冒称浏览器探针。
- 本票分支：`pi-web-agent-19d29c0a-d3ac-4961-aa99-638f8dbaa3a1`；唯一worktree：`C:/Work/AnotherFSM-worktrees/pi-web-agent-19d29c0a-d3ac-4961-aa99-638f8dbaa3a1`。受测实现提交、完整最终SHA及最新集成核验见外置 `environment.txt` / `handoff.txt` / `final-integration-after.log`。最终报告提交只改本文，不改变已验收实现。
- 日志、实际编译器诊断、JSON、浏览器测试产物（含存在的traces）、首次失败与核验脚本均在返回前保存至 `C:/work/anotherfsm-context-19/evidence-22/`；`tested-source-SHA256SUMS.txt`核对受测源文件，`SHA256SUMS.txt`核对外置证据，`CHECKSUM-VERIFIED.txt`保存实际校验结果。不依赖dirty或ignored文件作为唯一交付。
- 已再次合并最新本地集成tip并对照远端tip（均63308a7），无冲突；最终干净提交后停止。未push、关票、合用户主树、清他人worktree、委派、调用模型CLI或自行执行独立审查。主代理仍统一负责整分支双轴审查/后续调度；本票不宣称该独立审查已经完成。

## Final review #19 补修后的追溯与重新验收

前文“未发现前票缺陷”“#17–#21 均已交付”“无已知未满足项”均保留为当时的历史结论，不覆盖本节补验事实。主代理在最终整分支 Standards 审查亲读并亲自运行公开反例，确认 **#19 初始化取消目标错误 P1**：完整方案中的非起始 Next 正在 `InitBeforeStart` 协作等待自身 Token，但 Stop 只取消起点；Stop/start 因而互等且引擎不退出。主代理已重开原票 #19 并评论 #22；修复责任及真实红绿见 [#19 追加记录](19-execution-lifecycle.md)。后来完成的独立 Spec 全父规格审查为缺失/范围蔓延/实现错误/推测均 0（其独立267/API77/宿主36通过），但它不抵消本 P1，也不代替修复后的回归。本节不是新一轮自行审查或 #22 新功能实施。

### 责任与验收族补链

| 父规格验收族 / 原票责任 | 本次补充证据 |
| --- | --- |
| 引擎/节点归属及退出；数据/控制/所有权 → #19 | `ExecutionReviewFacts.StopCancelsTheNonStartInitializerAndDoesNotReleaseLaterNodes` 8 例，业务/无业务、可达/不可达 Next、Stop/Dispose；实际初始化被取消、未返回仍占用、普通准备拒绝、停止不准入后续初始化，业务不运行；原非协作及 inline 取消屏障全族保留 |
| 启动与共享；停止及旧轮次 → #19 基础、#20 受影响回归 | `SharedStartCancelsItsActiveInitializerAndStopBeforeAdmissionSkipsTheOtherInitializers` 4 例，失败保 Prepare、全接收先就绪、不共享控制、停止早于后一个引擎准入时零初始化；原 EventSharingFacts 30 例全量重跑 |
| API/真实 WASM合同 → #19 缺陷、#21 宿主受影响回归、#22 分层门禁 | API 及 RuntimeSession 两个 Retirement 族各新增非起始 Next × 业务/无业务 × Stop/Import/Dispose 6 例；保留锁外先取消再等 starting 和真正退出，不恢复普通启动抢占。同步初始化交错是核心/API/宿主.NET证据；实际 Chrome ReleaseWASM/API仍单列，不称新浏览器初始化探针 |
| 最终收缩与仓库闭合 → #22 | 未恢复旧 Context/构造/Restart/Handle，不新增 FSMState、不重做系统、不自动取消所有业务对象或 Dispose 借用资源；最终编译负例与所有消费者、前端、真实浏览器门禁重新执行 |

受测修复提交 **`9e228babeba30190ec9881c089ad0314c40a4276`**（`fix(#19): cancel the active initializer before awaiting exit`）；基线为主代理指定的 `4f78933db1d6f6bfe5fc2a956347b9b79739a27b` / tree `68db688fb569f33fefbbe485058c489ff5eb276f`。实现仅改 `FSMExecutor.cs`：既有锁内发布/清除活动初始化节点并关闭停止后的准入，锁外执行初始化及取消，保留已有退出和 inline 取消请求屏障。

原始证据目录固定为 **`C:/work/anotherfsm-context-19/evidence-final-fixes/`**，含 `01` 首次fixture失败、`02` 修正后8个目标红灯、`03` 8个绿灯、`04` 首次全量Debug、`run-gates.sh`、逐命令日志/退出文件、编译器原始诊断、TRX、浏览器日志/JSON。最早的4个业务fixture错误没有抹除；之后8个红灯均明确为实际初始化节点未取消。其余共享/宿主补充用例只作为受影响回归，不伪称另外取得生产红灯。

### 本次最终门禁（全部真实重跑）

`run-gates.sh` 是 Git Bash 后台测试脚本，不是代理；所有命令已结束并逐个保存退出码 **0**，`runner.exit=0`。JRE 与 npm shell 均按任务指定。以下日志均在上述外置目录；受测生产/测试源码为 `9e228ba`，其后只追加本票及 #19 的版本化记录。

| 层级 / 命令 | 本次结果 | 日志 |
| --- | --- | --- |
| 完整Debug构建：`dotnet build AnotherFSM.sln --no-incremental --nologo -p:NuGetAudit=false` | 0错误 / 66警告 | `final-debug-build.log` |
| 完整Debug测试：`dotnet test AnotherFSM.sln --no-build --no-restore --nologo` | 核心 **279** / API **83** / 宿主.NET **42**，0失败/跳过 | `final-debug-test.log` |
| 完整Release构建：同build加 `-c Release` | 0错误 / 66警告 | `final-release-build.log` |
| 完整Release测试：同test加 `-c Release` | 核心 **279** / API **83** / 宿主.NET **42**，0失败/跳过 | `final-release-test.log` |
| 真实类型编译：`bash tools/test-typed-context.sh` | 正例执行、生成消费者与8负例通过 | `final-typed-compiler.log`、`typed-compiler/` |
| 最终删除编译：`EVIDENCE_DIR=... bash tools/test-final-contract.sh` | 正例及12负例通过 | `final-removed-compiler.log`、`removed-compiler/` |
| 旧组合编译：`bash tools/test-removed-composition.sh` | 正例及9负例通过；共 **29** 个真实编译负例 | `final-composition-compiler.log`、`composition-compiler/` |
| 生成消费者：`dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | 编译、查询及真实Module流程成功 | `final-generated.log` |
| 原共享族：`dotnet test tests/StateMachineTest/StateMachineTest.csproj --no-build --no-restore --nologo --filter FullyQualifiedName~EventSharingFacts` | **30/30** | `final-sharing.log` |
| 前端：npm显式 `--script-shell='C:/Program Files/Git/bin/bash.exe'` 的 `ci --include=dev` / `test` / `run typecheck` / `run lint`，均指定前端prefix | 安装0、测试 **12/12**、typecheck/lint均0 | `final-npm-ci.log`、`final-frontend-{test,typecheck,lint}.log` |
| Release发布：`RUNTIME_CONTRACT=true bash tools/build-browser.sh` | 真实Release WASM publish及React生产构建成功 | `final-browser-build.log` |
| 真实Chrome：`PORT=5180 node tools/run-browser-tests.mjs` | **WASM12/12、API7/7**；两JSON均0 skipped/unexpected/flaky | `final-browser-runner.log`、`browser/{wasm,api}-results.json` |
| 证据校验：`node .../verify-results.mjs`，`git diff --check` | 均0；读取全部门禁退出文件、两配置三层计数、真实浏览器JSON；实连确认5079/5180均closed | `final-results-verified.log`、`verified-summary.json`、`final-diff-check.log` |

相比前文267/77/36，增加12/6/6个本缺陷回归；另2个既有“Stop早于round发布”用例收紧为不再准入初始化，没有删除该交错。焦点Release TRX另记录核心73/API26/宿主.NET12全部通过（`focal-regressions.log`、`focal-trx/`）。已有完整solution测试之外，该TRX保留具体场景名称供主代理复核。

已知限制不变：非协作初始化/节点/取消回调不返回仍保持占用；不强制中断或Dispose业务资源。完整构建66个警告包含既有nullable/打包/成员警告和确定性同步回归的xUnit1031（本次新增1个）；npm ci报告18 vulnerabilities（11 moderate、7 high），未升级依赖或改lock。Release保留反射/Reactive/Autofac裁剪及React chunk警告，浏览器真实结果不意味着无警告；同步初始化交错仍明确是.NET证据，非浏览器阻塞探针。

交付分支 `pi-web-agent-fffc95fc-a9c1-4ca2-ac5d-84418023c187`，唯一worktree `C:/Work/AnotherFSM-worktrees/pi-web-agent-fffc95fc-a9c1-4ca2-ac5d-84418023c187`。最终提交SHA、与最新集成tip的merge核验、干净状态及证据校验和保存在外置 `handoff.txt` / `final-integration.log` / `SHA256SUMS.txt`。主代理负责复核、重新关闭 #19 及父规格最终处置；实施者未push/关票/合主树、未另建worktree或运行自建/隐藏审查。

## 主代理最终独立闭合

最终补修合入集成 `25be4e39e552369e0a23298a9e9463f5f928a48d`（受测 tree `45db7458da37de79c7bc85486b1086b1ed60b0b9`）后，主代理亲自重做全部门禁：Debug/Release 核心 **279/API83/宿主42**，29个真实编译负例、生成消费者、前端12/typecheck/lint、重新Release publish及真实Chrome **WASM12/API7**，全部通过。两JSON无跳过/异常/flaky，服务端口均closed；正常生产包重建且排除合同页。原Standards复审确认P1闭合、补丁新增缺陷/硬违规/smell均0，Spec补丁复核新增缺陷/缺口/范围蔓延均0。所有已知发现有主代理逐条核验和处置，无剩余已知未满足AC。

完整主代理结果、两轴原始范围与最终结论、技术交付边界统一见 [#16 最终交付记录](16-final-delivery.md)。本矩阵仍保留每轮首次失败及分层证据，不以后续绿色覆盖历史；最终工单关闭及远端提交以GitHub交付回读为准。
