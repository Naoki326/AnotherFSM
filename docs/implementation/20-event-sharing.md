# #20 业务事件共享与协调启动

基线：`4063178bca93749ce966988920d349852e80108f`（#19 已验收）。测试接缝沿用父规格确认的核心公共装配/执行；真实节点、固定业务对象、队列观察和可控屏障，不使用随机 delay 推断时序。本票不承担 #21 宿主迁移，不新增消息平台或 FSMState。

## 阶段 1：接收先就绪、初始发布

- 新公共入口：`FSMEventSharing.Connect(params IFSMEventSharingExecution[])` 返回 void；成员是 `FSMContextPreparation` 或 `FSMExecution`，均指具体轮次。
- `FSMEventSharing.StartAsync(params FSMContextPreparation[])` 返回本次启动的 executors，不等待业务完成；原有 preparation `StartAsync` 形状保留并走同一屏障。
- 全部接收资源准备后提交轮次，再调用节点初始化/执行；复用 #19 的初始化与真正退出、取消回调屏障。
- 一次发布直接对范围快照 fan-out，不转发已接收事件；按目标完整本地事件名解析并携带独立事件副本。

实际 red→green：`dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo -p:NuGetAudit=false --filter FullyQualifiedName~EventSharingFacts`：先因缺少公共 API 编译失败（`01-red.log`）；首次 green 暴露测试 helper 的 ConnectNode 参数顺序错误（`01-green.log`），修正 helper 后 **1/1**（`01-green-rerun.log`）。#19 两族公开生命周期回归 **71/71**（`01-lifecycle.log`）。日志在 `.scratch/20/`，最终复制到 `C:/work/anotherfsm-context-19/evidence-20/`，防隔离 worktree 自动移除。

后续阶段和逐 AC 结果在最终交付前补齐。
