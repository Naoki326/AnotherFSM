# #20 业务事件共享与协调启动

> 后续验证更正：原“最终回归”是当时单次通过的历史记录，未证明 Module 身份测试无时序假设。主代理首次重构建发现 259 通过 / 1 失败；本轮确定性复现、修复和最新验证见文末“正式审查后的测试修复”。

基线：`4063178bca93749ce966988920d349852e80108f`（#19 已验收）。测试接缝沿用父规格确认的核心公共装配/执行；真实节点、固定业务对象、队列观察和可控屏障，不使用随机 delay 推断时序。本票不承担 #21 宿主迁移，不新增消息平台或 FSMState。

## 阶段 1：接收先就绪、初始发布

- 新公共入口：`FSMEventSharing.Connect(params IFSMEventSharingExecution[])` 返回 void；成员是 `FSMContextPreparation` 或 `FSMExecution`，均指具体轮次。
- `FSMEventSharing.StartAsync(params FSMContextPreparation[])` 返回本次启动的 executors，不等待业务完成；原有 preparation `StartAsync` 形状保留并走同一屏障。
- 全部接收资源准备后提交轮次，再调用节点初始化/执行；复用 #19 的初始化与真正退出、取消回调屏障。
- 一次发布直接对范围快照 fan-out，不转发已接收事件；按目标完整本地事件名解析并携带独立事件副本。

实际 red→green：`dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo -p:NuGetAudit=false --filter FullyQualifiedName~EventSharingFacts`：先因缺少公共 API 编译失败（`01-red.log`）；首次 green 暴露测试 helper 的 ConnectNode 参数顺序错误（`01-green.log`），修正 helper 后 **1/1**（`01-green-rerun.log`）。#19 两族公开生命周期回归 **71/71**（`01-lifecycle.log`）。日志在 `.scratch/20/`，最终复制到 `C:/work/anotherfsm-context-19/evidence-20/`，防隔离 worktree 自动移除。

## 阶段 2：失败原子性、范围与运行中加入

首阶段提交：`f7e5fb6`。

- 接收准备窄接缝为 preparation 实例级 internal `ReceiverPreparation`（friend test assembly）；仅负责本次接收资源准备/回收，不公开通用消息平台。业务异常不充当接收失败。
- 真正 red→green：回退的第一个 Dispose 抛错导致后续资源未清理且 Starting 残留（`02-behavior-red.log`），修复后全部回退并保留占用，可重试（`02-green.log`，2/2）。接收准备中图被更改仍放行（`04-red.log`），提交前全体重验后 5/5（`04-green.log`）。接收资源退出错误未进入 Completion（`05-red.log`），移入 #19 真退出清理屏障后含生命周期两族 77/77（`05-green.log`）。
- 增量合同测试首次已绿，不冒称 red：集合合并/幂等/中间退出、双向及重复主动通知、Connect 原子失败/克隆 ID 隔离（`03-sets.log`）；运行中准备失败/遗漏 pending/并发 Start、Connect、释放的拒绝/no backlog/旧成员不重启（`06-join-start-rerun.log`，8/8）。`06-join-start.log` 是测试 FluentAssertions 调用错误的编译失败，已修正，不算生产缺陷。
- 首次完整核心回归发现已有无装配入口的 `StartAsync(start,end)` 被提前校验误拒绝，11 失败（`core-mid.log`）；修正并新增 `WithEntry(start,end)` 允许无放行配置批量入口后 **239/239**（`core-mid-green.log`）。原 #19 调用形状保持有效。
- 真实反例：接收图未注册克隆事件，但 legacy 结束配置持有该外图克隆时，远程名称从结束配置 fallback 导致越过本地注册规则（`07-red.log`，ordinary 通过/end 失败）；远程只查本地图，保留单执行 legacy 本地事件路径后共享族 **10/10**（`07-green.log`）。

## 阶段 3：完整名称、通知身份与生命周期

阶段 2 提交：`d7325a4`。

- 真实 DSL Module 展开执行 8 个完整名称场景：`CheckA.OK` 精确普通/结束匹配，`CheckB.OK`、`checka.OK`、裸名 `OK` 不匹配。首次均已绿。新增 foreign end 配置同名不同 ID 的公开反例失败（`08-red.log`，8 通过/1 失败），构造时按目标本地完整名规范化结束配置后 **19/19**（`08-green.log`）。
- 主动业务响应再发布同名为新通知；暂停普通/结束排队；不合作节点停止仍占用且不接收/外发；新轮显式连接、旧节点路由不追新轮，增量首次均绿（`09-control-rounds.log`，23/23）。
- 新 `WithEntry` 后单执行无参 Start 被原始 entry 检查误拒绝（`10-red.log`），统一使用选定入口后通过（`10-green.log`）。
- 初始接收准备阻塞时全部节点零运行，失败后显式释放可重新 Prepare（`11-initial-barrier.log`）；批量 Start 不重读来源，跨图载荷不换绑、不改 canonical event（`12-fixed-bindings.log`）；公开 Finished 通知屏障挂起在途 fan-out，旧接收执行退出、新轮显式加入后仍不改投（`13-inflight-round.log`）；以上首次已绿。
- 遗漏成员诊断缺少具体身份（`14-red.log`），加入 pending 入口与 engine 身份后共享族 **28/28**（`14-green.log`）。共享暂停旧队列经 Stop/重连不带入新轮首次已绿（`15-old-queue.log`）。
- 独立中英使用说明：`docs/event-sharing.md`、`docs/event-sharing.EN.md`，未修改 #21 README/宿主说明。

## 公共 API

- `IFSMEventSharingExecution`：只用于 Connect 的具体 preparation/round 成员类型，不是可扩展消息平台；null 或自定义实现被拒绝。
- `FSMEventSharing.Connect(params IFSMEventSharingExecution[] executions) : void`。
- `FSMEventSharing.StartAsync(params FSMContextPreparation[] preparations) : Task<IReadOnlyList<FSMExecutor>>`，输入顺序对应返回顺序，不等待业务完成。
- `FSMContextPreparation.WithEntry(string startNode, string endEvent) : FSMContextPreparation`，无放行选择入口。
- #19 原有 Prepare、两个 preparation StartAsync 重载、Execution、Completion 和控制调用形状不变。单执行 Start 也遵守范围完整性屏障。

空 Connect/Start 拒绝，单成员合法；Connect 重复句柄幂等，Start 重复句柄拒绝。同批多个独立范围启动不隐式连接。已运行成员无需/不得重复 Start。活动范围在 Connect 合并；pending 在全体接收准备成功提交后正式加入。准备中的范围拒绝并发关系/重复启动/放弃变更，调用者可稍后重试。没有新 FSMState、父子执行或通用总线。

## 逐 AC 导航

以下名称均在 `tests/StateMachineTest/EventSharingFacts.cs`，除另注外均为本票新增公共装配/执行测试。负断言使用发布完成、队列后置 Fence、明确暂停/准备/退出屏障；超时只限制等待，不用 sleep 或内部字典证明时序。

| #20 AC | 实现与测试 |
| --- | --- |
| 1：具体执行、void、框架持有、双向、仅业务 | `OverlapIsIdempotentBidirectionalAndSurvivesAnIntermediateExitWithoutSharingContextOrControl`；`CoordinatedStartReusesFixedSourcesAndSharedPayloadsNeverReplaceTargetBindingsOrCanonicalEvents` |
| 2：集合合并、幂等、参数原子、默认隔离 | `Overlap...`；`FailedConnectCannotMergeRangesAndClonedEventIdsDoNotGrantMembership` |
| 3：成员退出不拆散、不继承 | `Overlap...`；`RestartedMembersNeedExplicitConnectionAndOldNodeRoutesCannotPublishIntoTheNewRound` |
| 4：完整名、普通/结束一致、非 ID 授权 | `ForeignClonedIdsCannotBypassTargetLocalNameRegistrationForOrdinaryOrEndEvents`（2）；`ARegisteredLocalEndNameDoesNotDependOnTheConfiguredForeignEventId`；Module 8 例；原 #19 `SameEventIdentityDoesNotAuthorizeBusinessTransitionsInAnUnconnectedEngine`、`ConcurrentEnginesWithTheSameEventIdentityDoNotFinishEachOther`（默认隔离） |
| 5：未知忽略、当前转移/结束、本地绑定、Module 不剥前缀 | `RealModuleOutputsKeepTheirFullCaseSensitiveLocalIdentityForOrdinaryAndEndEvents`（8）；`CoordinatedStartReuses...` |
| 6：同通知至多一次、主动新通知 | `Overlap...` 的自转移 Ping/Fence、重复声明和重复发布；`AnActiveBusinessResponseRepublishesTheSameNameAsANewNotification` |
| 7：全 pending 校验、不重复运行 | `StartRejectsInvalidOrIncompleteSelectionsWithoutConsumingValidPreparations`；`MissingSelectionDiagnosticsIdentifyThePendingEntryAndEngine`；`RunningRangeKeepsWorkingDuringFailedJoinAndNeverReplaysPreJoinNotifications`；同引擎两个有效 Prepare 已被 #19 占用入口拒绝，不伪造私有句柄 |
| 8：全体接收就绪才放行、首节点即时发布 | `AllReceiversAreReadyBeforeInitialNodesPublishAcrossIndependentLocalEventIds`；`InitialReceiverBarrierReleasesZeroNodesAndFailedPreparationsCanBeExplicitlyAbandoned` |
| 9：准备失败零放行/资源撤销/保留占用/重试或释放 | `ReceiverPreparationFailureReleasesAllTemporaryResourcesAndRetainsRetryableOwnership`；`AGraphInvalidatedDuringReceiverPreparationCannotReleaseAnyNode`；`InitialReceiverBarrier...`；`ReceiverCleanupErrorsAreInsideTheTrueExitBarrierAndCannotLeakEngineOwnership` |
| 10：运行中登记 pending、屏障加入、不重启旧成员 | `RunningRangeKeepsWorkingDuringFailedJoinAndNeverReplaysPreJoinNotifications` |
| 11：无加入前补发、失败保留旧共享、旧在途不改投 | `RunningRange...`；`ANotificationAlreadyInFlightCannotRetargetAReplacementThatJoinsBeforeDeliveryReturns`；`RestartedMembers...` |
| 12：暂停排队/结束不越过、停止拒收、真退出占用 | `PausedMembersQueueOrdinaryAndBusinessEndEventsWithoutControllingOtherMembers`；`StoppingRejectsNewEventsButKeepsEngineOwnershipUntilTheNonCooperativeNodeActuallyReturns`；`SharedEventsQueuedInAPausedOldRoundCannotSurviveStopAndExplicitReconnection`；原 #19 初始化/取消回调屏障完整回归 |
| 13：公共接缝、真实节点/同步点 | 上述共 29 新增用例；接收故障仅使用实例级 internal 窄接缝，不以节点业务异常替代。业务结果通过真实节点公开执行观察 |
| 14：说明与范围 | 独立双语说明；无宿主迁移、父子执行、持久化、可靠消息或前端功能 |

## 最终集成与完整回归

实际分支：`pi-web-agent-2e3d7d93-a946-45bd-884b-4d8d7d47ff52`。隔离 worktree：`C:/Work/AnotherFSM-worktrees/pi-web-agent-2e3d7d93-a946-45bd-884b-4d8d7d47ff52`。

验证代码提交：`1712a3f8c26ca085696176a06faaadd6c0690f75`（其后仅补本文最终证据）。实施前 HEAD、merge-base 和集成 tip 均为 `4063178bca93749ce966988920d349852e80108f`，工作树干净；实施后及完整回归后均执行 `git merge implement/context-lifecycle`，最新 tip 仍为该 SHA，Already up to date，无冲突、无 reset、未修改对方分支。阶段提交保留为 `f7e5fb6`、`d7325a4`、`1712a3f`。

所有 bash 显式超时 15–120 秒，构建串行，设置 `JAVA_HOME=C:/Users/Naoki/.jre/jdk-11.0.26+4-jre`。下表是最终生产代码上的实际运行，不沿用中间结果冒充最终回归。

| 最终命令 | 实际结果 | 原始日志 |
| --- | --- | --- |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo -p:NuGetAudit=false` | **260 通过，0 失败**（原核心 231 + 本票 29） | `final-core.log` |
| `dotnet test tests/FSMDemoApiTest/FSMDemoApiTest.csproj --nologo -p:NuGetAudit=false` | **39 通过，0 失败** | `final-api.log` |
| `dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --nologo -p:NuGetAudit=false` | **27 通过，0 失败**，仅宿主 .NET | `final-host.log` |
| `bash tools/test-typed-context.sh` | 正例编译/执行及 **7 真实编译负例通过** | `final-compile.log`；`compiler/*.txt` 保留各编译器原始诊断 |
| `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | 生成消费者编译、查询、真实 Module 执行通过 | `final-consumer.log` |
| `dotnet build AnotherFSM.sln -c Release --nologo -p:NuGetAudit=false` | **0 错误，65 警告** | `final-release.log` |
| `git diff --check` | 通过 | `final-diff.log` |
| `git merge implement/context-lifecycle`（两次） | Already up to date；merge-base 为 `4063178` | `final-integration-merge.log`、`final-integration-recheck.log` |

原始红/绿、中间失败、最终测试与编译诊断在返回前复制到 **`C:/work/anotherfsm-context-19/evidence-20/`**，不依赖可能自动移除的 worktree。没有复制 bin/obj。`handoff.txt` 记录最终分支/SHA及 worktree，`SHA256SUMS.txt` 校验保存日志。

已知验证边界：未运行真实 Release WASM 浏览器合同，本票没有修改宿主/前端，不以宿主 .NET 或 Release 构建冒充浏览器证据；该迁移属于并行 #21。65 警告包含既有警告与 3 个有意的 xUnit1031（仅 Task.Run 中接收准备/公开状态通知的确定性阻塞屏障，均由 finally 释放；不以延时证明时序）。无已知未满足 #20 AC。

全程自行 read/edit/write/bash/test/commit，未委派、未调用 pi/claude/模型 CLI、未启动隐藏代理、未执行独立审查流程、未 sleep 轮询；未 push、关闭票、合主 worktree 或继续后票。

## 正式审查后的测试修复

本轮仅使用 `C:/Work/AnotherFSM-worktrees/review-20`，沿用分支 `pi-web-agent-2e3d7d93-a946-45bd-884b-4d8d7d47ff52`，起点 `13d4ae0b06e4b7bb907bc9ed0898f1c281f8df6c`。用户转交正式审查：Standards 硬违规/新增正确性问题为 0，仅有一项低优先重复入口维护意见；Spec 除已确认测试问题外，无明确缺失、范围蔓延或实现错误。本轮不自建审查、不等待后续通知。

### 已核实失败与根因

- 主代理首次重构建核心 **259 通过、1 失败**：`RealModuleOutputsKeepTheirFullCaseSensitiveLocalIdentityForOrdinaryAndEndEvents("CheckA", true)` 等目标初始 `start` 超时（原 `.scratch/main-20/core.log`）。同用例单独三次均 8/8，不能以重跑绿覆盖首次失败。
- 主代理的公开真实 Module 复现（`main-module-20-repro/Program.cs`、`run.log`）：目标 `InitBeforeStart` 等源初始发布完成；源发布成功，目标 Finished、初始 Execute 次数 0、Completion RanToCompletion。事件没有丢失；AC8 只保证全体接收先就绪，不保证目标初始业务先执行。已经 Finished 的执行不必再跑初始业务。
- 本轮先将同一交错固定进原身份用例：目标公共 `InitBeforeStart` 等源发布，Start 放到 `Task.Run` 避免阻塞测试上下文。原断言确定性红：**7 通过、1 失败**，仍是目标 `start` 超时（`01-fixed-interleaving-red.log`）。这是测试的无序假设，不是生产事件投递故障。

### 最小修复与规范正例

- 身份用例仍保留完整 8 场景及原有身份/次数断言，只将其源业务发布显式门控到目标已进入初始 Execute；不再借调度顺序验证身份。
- 新增 `ARealModuleEndReceivedBeforeTheInitialRunFinishesWithoutExecutingAndReleasesOwnership`：真实 DSL Module、真实绑定和公开 `InitBeforeStart` 同步点固定 end 早于目标初始 Run。断言源发布完成、目标 **零 Execute/无业务访问、Finished、Completion 成功**；未先 Stop/Dispose 旧轮次就能重新 Prepare 同一引擎/绑定节点，新轮真实执行并通过本地出口完成，源执行仍 Running，证明退出后占用/绑定可复用且完成不控制其他成员。
- 原立即发布与全体接收屏障专项测试保留不动，包括 `AllReceiversAreReadyBeforeInitialNodesPublishAcrossIndependentLocalEventIds`、`InitialReceiverBarrierReleasesZeroNodesAndFailedPreparationsCanBeExplicitlyAbandoned`。不放宽断言、不删测试、不改生产强制运行已 Finished 节点。
- 红后首绿：Module 族 **9/9**（`02-module-green.log`）；新增早到 end 是原失败交错的规范正例，不冒称另发现生产红例。
- 采纳 Standards 维护意见：全仓 C# 搜索确认 internal `FSMExecutor.StartContext` 仅有定义、没有调用；#20 全部走 `PrepareContext`。仅删除该重复未使用方法，不新增抽象、不删公共过渡 API、不改 #22。删除前后引用证据在 `context-entry-references-before.log`、`context-entry-references.log`。

### 本轮最终验证与集成

所有命令显式 timeout 15–120 秒，构建串行，`JAVA_HOME=C:/Users/Naoki/.jre/jdk-11.0.26+4-jre`。已执行 `git merge 4063178bca93749ce966988920d349852e80108f`，Already up to date；集成分支 `implement/context-lifecycle` tip 和 merge-base 均为 `4063178`（`integration.log`）。未改主工作树、未建额外 worktree。

| 命令/范围 | 结果 | 日志 |
| --- | --- | --- |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo -p:NuGetAudit=false` | **261/261**（原 231 + #20 30） | `03-core.log` |
| 同核心 `--no-build --no-restore`，固定重复 3 次 | 每次 **261/261** | `05-core-repeat-{1..3}.log` |
| 核心 `--no-build --no-restore --filter FullyQualifiedName~EventSharingFacts`，固定重复 10 次 | 每次 **30/30**，含 Module 9 例及既有立即发布测试 | `04-sharing-repeat-{1..10}.log` |
| `dotnet test tests/FSMDemoApiTest/FSMDemoApiTest.csproj --nologo -p:NuGetAudit=false` | **39/39** | `06-api.log` |
| `dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --nologo -p:NuGetAudit=false` | **27/27**，仅宿主 .NET | `07-host.log` |
| `git diff --check` | 通过 | `final-diff.log` |

本轮日志及主代理首次失败/复现原件复制到 `C:/work/anotherfsm-context-19/evidence-20-fixes/`，包含 `handoff.txt` 和 SHA256 清单。没有重跑真实 WASM 浏览器合同；不以宿主 .NET 测试冒充浏览器证据。既有警告保留，本轮没有新增已知生产缺陷。未 push、关票、合主树或继续其他票。

### 主代理最终核验与正式双轴结果

主代理对修复提交 `73f176e304cf0a0885fe064a12879bcc4823f90a` 阅读完整测试补丁及生产方法删除，重新构建运行：核心 **261/261**、API **39/39**、宿主 .NET **27/27**；日志 `.scratch/main-20/{core,api,host}-fixed.log`。首次失败、外部确定性复现和修复前固定红例均已保留，未用后续绿色抹去。

正式可见 `code-reviewer` 的 Spec 轴独立确认：AC8 保证接收先就绪而非初始节点必先执行，因此早到 end 已正常接收/完成不算丢事件；支持修正身份测试同步及增加受控早到 end 回归；除已确认测试问题外，缺失/范围蔓延/错误实现均 **0** 明确发现。Standards 轴：文档化硬违规 **0**、新增正确性问题 **0**、推测 **0**；唯一低优先 Duplicated Code 意见已采纳，删除已被分阶段入口替代且全仓没有引用的 internal StartContext。主代理逐条核验处置，无待处理的本票已知发现。此记录不是 #21 浏览器迁移或父规格最终验收。
