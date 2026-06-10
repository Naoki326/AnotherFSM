# CONTEXT — AnotherFSM 领域词汇表

> 这是项目的 **Glossary**，不包含实现细节、spec 或临时设计记录。

## 核心概念

### FSMEngine
整个状态机的运行时容器。持有所有 Node 和 Event 的扁平字典（`nodeDict`、`eventDict`）。所有 Node 共享同一个 `FSMEngine` 引用。

### FSMExecutor
状态机执行器。以起始 Node 和结束 Event 构造，在 `FSMEngine` 的子图上驱动 Node 的依次执行。管理暂停/继续/停止生命周期。一个 `FSMEngine` 可以同时运行多个 `FSMExecutor`（如 `ParallelNode` 场景）。

### IFSMNode（节点 / Node）
状态机中的一个执行单元。拥有 branch 定义（`SetBranchEvent(index, event)`）和 transition 列表。执行完毕后通过 `PublishEvent(index)` 触发转移事件。

### FSMEvent（事件 / Event）
触发状态转移的信号。每个 Event 有一个全局唯一的 `EventID`。Node 通过 `AddTransition(event, target)` 注册转移规则。

### FSMTransition（转移 / Transition）
三元组 `(Source Node, Trigger Event, Target Node)`。当 Source Node 是当前节点且 Trigger Event 被发布时，引擎将当前节点切换到 Target Node。

### Branch
Node 内部的「分支 → 事件」映射。一个 Node 可定义多个 branch（如 `1->NextEvent; 3->ErrorEvent;`），每个 branch 将一个索引（int）绑定到一个 Event。Node 执行时通过 `PublishEvent(index)` 发出对应 Event。

### GroupNode
运行时子图包装节点。指定子图的起始节点名和结束事件名，创建子 `FSMExecutor` 运行。**所有节点必须在同一个 `FSMEngine` 的字典中**——不是真正的模块边界。

### ParallelNode
运行时并行子图包装节点。持有多个 `{StartNode, EndEvent}` 对，每个创建独立的 `FSMExecutor` 并行执行。同样依赖同一个 `FSMEngine`。

### Namespace（已有语法）
ANTRL 语法中的 `namespace Foo { ... }`。仅在所有 Node/Event 名前添加 `Foo.` 前缀，不提供封装或可见性控制。

---

## 模块系统（设计阶段，待实现）

### 模块（Module）
编译期的状态机复用单元。一个 `.fsm` 文件定义一个 `module`，包含一组 Node、Event、Transition，并声明 Input（入口节点）和 Output（出口事件）。

**关键属性**：
- 纯编译期概念，运行时扁平展开到父 `FSMEngine`
- 不改变 `FSMExecutor` 的执行模型
- 目标是拆分复杂度，不新增运行时能力
- 同一模块可被多次引入（不同实例前缀）

### Input（入口节点）
模块暴露的公开入口节点。外部可通过带前缀的节点名（如 `Sub1.Check`）直接连线到入口节点。`input` 声明只是可见性标签——入口节点在模块内部仍是普通 Node。

### Output（出口事件）
模块暴露的公开事件。内部节点发出 Output 事件后，通过实例 body 中的事件映射（`Done->GoEvent`）转发为父作用域的普通 Event。

### 模块实例（Module Instance）
通过 `def Sub1(SubFlow) { ... }` 创建的模块使用。实例名（如 `Sub1`）用作编译期前缀，并为内部所有 Node/Event 命名空间隔离。模块实例本身**不作为运行时节点存入 nodeDict**，但在可视化中有一个**视觉代理节点**（仅用于流程图渲染，含 Pos、Color 等属性）。

### Event Mapping（事件映射）
模块实例 body 中的 `OutputEvent->ExternalEvent` 声明。编译期将模块内部发出 OutputEvent 的 Node 的 branch 改绑为 ExternalEvent，使得外部 Event 直接参与父 `FSMEngine` 的转移。

### Module Registry
`FSMEngine` 上的模块预注册表。模块内容在 `Build()` 前通过脚本字符串或文件路径注册，Build 时按需展开。

### Transition 占位符
父脚本中 `Event->ModuleInstanceName to TargetNode` 里的 `ModuleInstanceName`（如 `GoEvent->Sub1 to Next`）。编译器展开时替换为模块内部实际发出该事件的 Node 名。

---

## 区分细微概念

- **Module vs GroupNode**：Module 是编译期复用，GroupNode 是运行时包装。Module 展开后内部节点与父引擎平级存在；GroupNode 创建子 Executor 运行子图。
- **Module Instance vs Node Instance**：模块实例不在 nodeDict 中，不参与执行；Node 实例在 nodeDict 中，是执行单元。
- **Input/Output vs Branch/Transition**：Input/Output 是模块级接口声明；Branch 是 Node 内的分支绑定；Transition 是 Node 间的转移线。
