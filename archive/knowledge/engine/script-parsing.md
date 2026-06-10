---
name: script-parsing
description: 脚本两 pass 编译流程：BuildStateVisitor + BuildTransitionVisitor
metadata:
  type: knowledge
---

# 脚本编译流程

`BuildStateVisitor` `BuildTransitionVisitor` — ANTLR 脚本的两 pass 编译

## 编译管线

```
脚本字符串 → ANTLR Lexer/Parser → AST
     │
     ├─ Pass 1: BuildStateVisitor ─→ 创建节点、事件、模块实例
     │
     └─ Pass 2: BuildTransitionVisitor ─→ 建立节点间连线
```

## Pass 1 — BuildStateVisitor

**职责**：遍历 AST，创建所有节点实例和事件，展开模块。

### 关键 Visitor 方法

| 方法 | 处理内容 |
|------|----------|
| `VisitDefState` | `def NodeName as NodeType state { ... }` — 创建节点 |
| `VisitDefState2` | `def NodeName(NodeType) { ... }` — 简写语法，也检测模块类型 |
| `VisitDefBranch` | `1->NextEvent;` — 为节点设置 branch 事件绑定 |
| `VisitPosDef` | `Pos:(x,y);` — 设置节点位置 |
| `VisitColorDef` / `VisitTypeDef` / `VisitFlowIDDef` | 其他节点属性 |
| `VisitImport_statement` | 加载 .fsm 模块文件 |
| `VisitModule_statement` | 解析模块定义 |
| `ExpandModuleInstance` | 四阶段展开模块实例（见 [[module-expansion-pipeline]]） |
| `VisitGroudFSMDef` | `[Event->Node];` — GroupNode 内嵌状态机定义 |

### 命名空间

`namespace Foo { ... }` 内的节点名自动加 `Foo.` 前缀。嵌套时前缀叠加：`NS.Sub.Node`。

### 节点创建流程

1. 通过 `IFSMNodeFactory.CreateNode(typeName)` 创建实例
2. 设置 `Name`/`NamePrev`（节点名/命名空间前缀）
3. 解析 body 中的 branch 定义，调用 `SetBranchEvent(index, event)`
4. 设置 Pos/Color/Type/FlowID 等视觉属性
5. 加入 `nodeDict` 和 `eventDict`

## Pass 2 — BuildTransitionVisitor

**职责**：遍历 AST，建立节点间的转移关系。

### 关键 Visitor 方法

| 方法 | 处理内容 |
|------|----------|
| `VisitDefTransition` | `Event->FromNode to ToNode;` — 建立转移 |
| `ResolveSourceNodes` | 解析源节点（支持模块实例名展开） |
| `ResolveTargetNodes` | 解析目标节点 |

### 模块实例连线处理

- **fromNode 是模块实例名**：从 `ExternalEventToInternalNodes` 找到内部节点，为每个创建 transition
- **toNode 是模块实例名**：抛出 ScriptException（必须连线到具体节点）
- **正常情况**：直接 `AddTransition`

### Transform 模式

`Transform()` / `TransformByFile()` 只执行 Pass 2，不重新创建节点。用于运行时动态修改连线。需先清理 `moduleInstances`。

## 关键约束

1. **`UpdateEventDescriptions()` 必须在读取 `EventDescriptions` 前调用** — `SetBranchEvent` 只写 branchDict，不更新描述
2. **`isParsingModuleDefinition` 必须 try/finally 保护** — 异常时恢复标志位
3. **节点名不能以 `.` 结尾** — `FSMEngine_Function.CreateNode` 会校验

## 交叉引用

- [[core-architecture]] — 三种构建方式总览
- [[module-expansion-pipeline]] — 模块展开四阶段详情
- [[grammar-reference]] — .g4 完整文法定义
