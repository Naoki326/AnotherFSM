# #20 业务事件共享与协调启动

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

后续完整名称/Module、暂停/停止/旧轮次与逐 AC 结果在最终交付前补齐。
