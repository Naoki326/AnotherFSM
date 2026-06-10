---
name: composition
description: GroupNode/ParallelNode 组合模式：子图嵌套与并行执行
metadata:
  type: knowledge
---

# 组合节点：GroupNode 与 ParallelNode

`GroupNode` `ParallelNode` `BaseGroupNode` — 运行时子图组合模式

## 设计模式

组合模式：将子状态机封装为单个节点，对外表现为普通 IFSMNode。

```
BaseGroupNode (抽象基类, AsyncEnumFSMNode)
├── GroupNode       ← 单个子状态机
└── ParallelNode    ← 多个子状态机并行
```

BaseGroupNode 实现 `IObservable<StateTrackInfo>` 和 `IObservable<ExecuteTrackInfo>`，支持状态追踪。

## GroupNode — 单子图

封装一个子状态机的执行：

```csharp
// 脚本内嵌语法
[StartEvent->StartNode];  // 定义 GroupNode

// C# 定义
var group = new GroupNode {
    StartNode = "subStart",
    EndEvent = "subDone"
};
```

### 执行流程

1. `InitBeforeStart()` — 创建子 FSMExecutor，设置 Context 和 Tracker
2. `ExecuteEnumerable()` — 启动子 Executor，等待 endEvent
3. 子 Executor 完成后发布 `FSMEnum.Next`

### 关键属性

| 属性 | 含义 |
|------|------|
| `StartNode` | 子状态机起始节点名 |
| `EndEvent` | 子状态机结束事件名 |
| `ContextData` | 传递给子节点的上下文数据 |

### 特点

- 子状态机节点**共享同一个 FSMEngine**（不是独立的）
- 子 Executor 有独立的 CancellationToken
- 支持泛型 `GroupNode<T>` 匹配上下文类型

## ParallelNode — 并行子图

同时执行多个子状态机：

```csharp
var parallel = new ParallelNode {
    FSMs = [
        new() { StartNode = "taskA", EndEvent = "doneA" },
        new() { StartNode = "taskB", EndEvent = "doneB" }
    ]
};
```

### 执行流程

1. `InitBeforeStart()` — 为每个子图创建独立 FSMExecutor
2. `ExecuteEnumerable()` — 使用 `Task.WhenAll` 并行启动所有子 Executor
3. 任一子 Executor 停止 → 取消所有其他子 Executor
4. 全部完成 → 发布 `FSMEnum.Next`

### 协调机制

- **暂停传播**：暂停 ParallelNode 时，暂停所有子 Executor
- **取消传播**：任一子 Executor 停止，取消其余的 CancellationToken
- **泛型支持**：`ParallelNode<T>`、`ParallelNode<T, U>` 匹配不同上下文类型

## GroupNode vs Module

| 维度 | GroupNode | Module |
|------|-----------|--------|
| 时机 | 运行时 | 编译期 |
| 引擎 | 共享父 FSMEngine | 展平到父 FSMEngine |
| 隔离 | 独立 Executor | 无运行时边界 |
| 复用 | 不支持 | 支持 import 多次实例化 |
| 嵌套 | 支持（Executor 内创建 Executor） | 当前仅单层 |

## 交叉引用

- [[core-architecture]] — FSMEngine 和 Executor 关系
- [[scheduling-lifecycle]] — FSMExecutor 执行器详情
- [[hierarchy-interfaces]] — 节点继承体系
- [[module-expansion-pipeline]] — 编译期模块展开
