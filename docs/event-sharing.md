# 业务事件共享与协调启动

[English](event-sharing.EN.md)

共享只传递业务事件，不传递节点 Context、暂停、继续或停止。不连接的执行默认隔离，即使事件同名、共用对象或 Clone 保留了相同 EventID。

## 准备、连接、启动

```csharp
var flowA = engineA.BindContext<InputA>("Start", "Done");
var flowB = engineB.BindContext<InputB>("Start", "Done");
using var a = flowA.Prepare(inputA);
using var b = flowB.Prepare(inputB);
FSMEventSharing.Connect(a, b);                    // void；框架持有共享关系
var started = await FSMEventSharing.StartAsync(a, b);
var runningA = started[0].Execution;
var runningB = started[1].Execution;
runningA.PublishEvent("OrderReady", payload);
// 各执行独立控制和等待真正退出：
await runningA.StopAsync();
await runningB.StopAsync();
```

`Prepare` 固定本轮业务引用并占用引擎；未启动的 preparation 必须显式 Dispose。已启动后 Dispose preparation 不替代执行的 Stop/Completion。业务对象仍归调用方所有。

整个引擎无业务依赖时直接 `engine.Prepare().WithEntry("Start", "Done")`，不制造聚合对象；与业务 preparation 可同批连接/启动。没有在 BindContext 配置入口时，可以先 `preparation.WithEntry("Start", "Done")` 再批量启动。已有 `preparation.StartAsync()`、`StartAsync(startNode, endEvent, isLongRunning)` 仍有效，但也走相同集合屏障：不能逐个启动已连接的待启动范围。

## 范围、通知与名称

- `Connect(params IFSMEventSharingExecution[])` 接受 preparation 或具体 `FSMExecution`，返回 void；不接受 engine/current 别名，也不要求 link、using 连接或逐事件映射。
- 重叠范围合并；重复成员/重复 Connect 幂等。null、空集合、非法/已释放/停止中/退出成员会拒绝，失败不留下部分合并。单成员 Connect 合法。
- 成员退出仅移除自身；其余成员不拆分。旧成员身份不自动迁移到同引擎下一轮。
- 跨图按**区分大小写的完整事件名**查目标图本地事件，再按当前节点转移和本轮结束配置响应。未知名称忽略。`CheckA.OK` 不等于 `CheckB.OK`、`checka.OK` 或裸名 `OK`；Module 仍是编译期展平，不增加别名。
- EventID 是图内身份，不是共享授权。外图/克隆 ID 不能绕过目标本地名称注册；普通事件与结束事件规则一致。接收不改绑定，也不改 canonical event 的载荷；每次通知使用事件副本，业务载荷对象不深拷贝。
- 每次发布直接对当时已加入成员的快照各投递至多一次；接收不会自动转发。业务再次 Publish 同名/同 ID/同对象是新通知。没有可靠送达、离线重放或消除业务反馈循环的承诺。

## 接收屏障、失败与重试

Start 的参数只列本次待启动成员，但必须覆盖所涉及范围的**全部 pending**。重复 preparation（同引擎）、失效/已启动句柄、缺少入口、图变化或遗漏成员都会拒绝，不隐式替换运行执行。多个独立范围可以在同次 Start 中启动，不因此互相连接。

全部成员先检查，再完成可回退的接收准备；提交全部接收与成员可见性后，才放行任何本次节点。初始节点立即发出的通知可进入其他就绪接收队列；不承诺物理时间同时起跑，也不要求目标初始业务先执行。业务 end 早于目标初始 Run 时可正常 Finished、零业务执行并完成真退出。Start 返回启动结果，不是业务全体完成汇合。

检查/接收准备失败时零节点放行，撤销临时接收资源，保留有效 Prepare 和引擎占用。调用方可修正并用同一组句柄重试，也可显式 Dispose 放弃。诊断标识失败阶段，遗漏诊断包含入口和引擎；根因通过 InnerException 保留。放行后的初始化/业务异常遵守各自 #19 生命周期，不承诺全体事务回滚。同步用户初始化、取消回调或不合作节点不返回时，Completion 仍等待真正退出。

## 运行中加入、暂停与退出

```csharp
using var b2 = flowB.Prepare(newInputB);           // 旧 b 已真正退出
FSMEventSharing.Connect(runningA, b2);           // 只登记 pending
var added = await FSMEventSharing.StartAsync(b2); // 范围有其他 pending 时也必须一并列出
```

Connect 合并已有活动成员的范围立即生效；新 pending 成员只有 Start 接收屏障成功后才正式加入。准备期间旧成员继续共享，不重启、不换绑；新成员不接收加入前通知，失败后也不补发临时队列。加入失败不破坏旧成员共享。Start 正在准备的范围拒绝并发 Connect/再次 Start/释放待启动句柄，可在尝试完成后重试。

暂停成员仍接收本轮队列，包括业务结束事件；Continue 后处理。停止中/退出后拒收，停止一方不停止其他成员。接收资源和成员清理纳入 Completion 真退出屏障；Finished、Stop 或 Dispose 返回不授权提前复用引擎。旧句柄主动操作可观察地失败；捕获的旧节点路由、旧队列以及在途通知不能改投新轮次。

API 历史交付见 [#20 实施记录](implementation/20-event-sharing.md)，收缩后全族真实结果见[最终验收](implementation/22-final-acceptance.md)。不提供父子执行、ParallelModule、完成汇合、消息持久化或前端通信面板。
