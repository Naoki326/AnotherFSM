---
name: scheduling-lifecycle
description: FSMExecutor 事件驱动调度、Channel 队列、生命周期管理
metadata:
  type: knowledge
---

# FSMExecutor 调度与生命周期

`FSMExecutor` `FSMState` `Channel<FSMEvent>` — 事件驱动执行器的调度逻辑

## 核心调度模型

```
                  ┌──────────┐
   PublishEvent ─→│ Channel  │←─ 外部 Handle(event)
                  │<FSMEvent>│
                  └────┬─────┘
                       │ ConsumerTask 循环
                       ▼
              ┌────────────────┐
              │ RunCurrentNode │──→ node.RunAsync()
              └───────┬────────┘
                      │ 节点完成
                      ▼
              检查 transition ──→ 切换到目标节点
```

## 执行流程

1. **初始化**：以 `startNode` + `endEvent` 构造，创建 `Channel<FSMEvent>` 事件队列
2. **启动**：`Start()` → 调用 `RunCurrentNodeAsync()` 执行起始节点，同时启动 `ConsumerTask` 监听事件
3. **事件消费**：`ConsumerTask` 从 Channel 读取事件 → 检查当前节点是否有对应 transition → 切换节点
4. **结束**：收到 endEvent 或无匹配 transition → FSMState.Stopped

## 生命周期状态

```
          Start()
None ──────────→ Running
                    │  │  │
          Pause()  │  │  │ Stop()
                    │  │  │
                    ▼  │  ▼
                 Paused │  Stopped
                    │  │
          Continue()│  │
                    └──┘
```

| 状态 | 含义 |
|------|------|
| `None` | 未启动 |
| `Running` | 正在执行节点 |
| `Paused` | 暂停（节点内部 yield 点会检查） |
| `Stopped` | 已终止 |

## 关键方法

| 方法 | 作用 |
|------|------|
| `Start()` | 启动执行器，运行初始节点 |
| `Handle(FSMEvent)` | 向 Channel 投递事件 |
| `Pause()` | 设置暂停标志，节点在 yield 点响应 |
| `Continue()` | 恢复执行 |
| `Stop()` | 终止执行，取消 CancellationToken |
| `RestartAsync()` | 从起始节点重新执行 |

## 暂停机制

暂停不是立即中断，而是协作式：
1. `Pause()` 设置状态为 Paused
2. 节点在 yield 点（`IYieldAction.BeforeNextAsync`）检查暂停状态
3. YieldPriority 检查暂停锚点是否匹配
4. YieldPause / YieldRestoreIfPaused 响应暂停/恢复

## 可观察性

`FSMExecutor_Observable.cs` 实现 `IObservable<StateTrackInfo>` 和 `IObservable<ExecuteTrackInfo>`：
- `TrackStateEnter/Exit` — 节点进入/退出通知
- `TrackFSMStateChanged` — 执行器状态变化通知
- 通过 `IFSMNodeTracker` 接口订阅

## FSMSyncContext

自定义 `SynchronizationContext`，控制节点执行的线程调度。支持 Post 投递工作项。

## 与 FSMEngine 的关系

- 一个 FSMEngine 可同时运行多个 FSMExecutor（如 ParallelNode 场景）
- 所有 Executor 共享同一个 nodeDict/eventDict
- Executor 不拥有节点，只引用

## 交叉引用

- [[core-architecture]] — FSMEngine 整体架构
- [[instruction-system]] — Yield 指令与暂停点
- [[hierarchy-interfaces]] — IFSMNode.RunAsync 执行接口
- [[composition]] — ParallelNode 多 Executor 并行
