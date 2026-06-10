---
name: core-architecture
description: FSMEngine 整体架构、三种构建方式、数据流和核心字典
metadata:
  type: knowledge
---

# FSMEngine 核心架构

`FSMEngine` `FSMExecutor` `nodeDict` `eventDict` — 状态机运行时容器与构建方式

## 整体架构

```
┌─────────────────────────────────────────────────┐
│                   FSMEngine                      │
│  ┌──────────────┐  ┌──────────────────────────┐  │
│  │  nodeDict    │  │  eventDict               │  │
│  │ (string →    │  │ (string →                │  │
│  │  IFSMNode)   │  │  FSMEvent)               │  │
│  └──────────────┘  └──────────────────────────┘  │
│  ┌──────────────────────────────────────────────┐│
│  │  moduleRegistry (string → FsmModuleInfo)     ││
│  │  moduleInstances (string → FsmModuleInstance)││
│  └──────────────────────────────────────────────┘│
└─────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────┐
│  FSMExecutor     │  ← 以 startNode + endEvent 构造
│  Channel<Event>  │  ← 事件驱动调度
│  State: Run/Pause/Stop
└─────────────────┘
```

## 核心数据结构

- `nodeDict: Dictionary<string, IFSMNode>` — 所有节点的扁平字典，key 是节点名（含命名空间前缀）
- `eventDict: Dictionary<string, FSMEvent>` — 所有事件的扁平字典
- `moduleRegistry` — 模块定义注册表（编译期使用）
- `moduleInstances` — 模块实例映射（编译期使用，运行时不参与执行）

## 三种构建方式

### 1. 脚本方式 (`FSMEngine_Script`)
```csharp
engine.CreateStateMachine(scriptString, directoryPath);
engine.CreateStateMachineByFile("main.fsm");
```
通过 ANTLR 解析脚本 → `BuildStateVisitor` Pass 1 创建节点/事件 → `BuildTransitionVisitor` Pass 2 建立连线。

### 2. 编程 API (`FSMEngine_Function`)
```csharp
engine.CreateNode("Start", "startNode");
engine.CreateNode("Idle", "idleNode");
engine.ConnectNode("startNode", 1, idleNode, "NextEvent");
```
直接操作 nodeDict/eventDict 的命令式 API。

### 3. Fluent API (`FSMDefineBuilder_FluentApi`)
```csharp
var builder = engine.NewBuilder();
builder.AddNode("Start", "startNode")
       .AddNode("Idle", "idleNode")
       .AddConnection("NextEvent", "startNode", "idleNode")
       .Build();
```
构建器模式，最终调 `Build()` 写入 engine。

## 文件组成

| 文件 | 职责 |
|------|------|
| `FSMEngine.cs` | 核心类：nodeDict/eventDict 管理、ScriptNode/ScriptEvent 包装 |
| `FSMEngine_Function.cs` | CreateNode/ConnectNode/DeleteTransition 等命令式 API |
| `FSMEngine_Script.cs` | CreateStateMachine/Transform 脚本入口 |
| `FSMEngine_Module.cs` | 模块注册、依赖加载、循环引用检测 |

## 关键设计决策

1. **扁平字典**：所有节点在同一层级，模块展开后也是平级的。不存在运行时的树形结构。
2. **事件驱动**：节点执行完后通过 `PublishEvent` 触发转移，FSMExecutor 通过 Channel 接收事件。
3. **子图共享引擎**：GroupNode/ParallelNode 的子状态机也在同一个 FSMEngine 中，创建独立 FSMExecutor 但共享 nodeDict。

## 交叉引用

- [[script-parsing]] — 两 pass 编译详细流程
- [[module-expansion-pipeline]] — 模块展开管道
- [[scheduling-lifecycle]] — FSMExecutor 调度逻辑
- [[hierarchy-interfaces]] — IFSMNode 接口定义
