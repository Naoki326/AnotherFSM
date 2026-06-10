---
name: fluent-api
description: FSMDefineBuilder/FSMEngineBuilder 构建器模式与模块配置
metadata:
  type: knowledge
---

# Fluent API 构建器

`FSMDefineBuilder` `FSMEngineBuilder` `IFSMModuleConfigurator` — 流式 API 构建状态机

## 接口层次

```
IFSMDefineBuilder
├── IFSMNodeDefineBuilder    ← 节点级操作（SetBranchEvent 等）
├── IFSMTransformBuilder     ← 重新连线（不创建节点）
└── IFSMModuleConfigurator   ← 模块实例配置（MapOutput 等）

FSMEngineBuilder              ← 顶层入口，持有 FSMEngine 引用
FSMDefineBuilder : IFSMDefineBuilder
FSMTransformBuilder : IFSMTransformBuilder
```

## 核心用法

### 定义流程
```csharp
var builder = engine.NewBuilder();
builder.AddNode("Start", "start")
       .AddNode("Idle", "idle")
       .AddConnection("NextEvent", "start", "idle")
       .AddModule("SubFlow", "sub1", cfg => cfg
           .MapOutput("Done", "GoEvent"))
       .Build();
```

### Transform（重新连线）
```csharp
var transform = engine.NewTransformBuilder();
transform.AddConnection("NewEvent", "nodeA", "nodeB")
         .DeleteConnection("OldEvent", "nodeA")
         .Build();
```

### 模块实例配置

`IFSMModuleConfigurator` 提供：
- `MapOutput(string internalOutput, string externalEvent)` — 输出事件映射
- `SetPosition(double x, double y)` — 视觉位置
- `SetColor(string color)` — 颜色
- `SetFlowID(string id)` — 流 ID

## 与脚本路径的关系

Fluent API 的 `ExpandModuleInstances` 与脚本路径共享展开逻辑。关键差异：Fluent API 通过 `FSMEngineBuilder.Build()` 触发，nodeDict 操作直接走 engine API 而非 ANTLR Visitor。

## 交叉引用

- [[core-architecture]] — 三种构建方式总览
- [[module-expansion-pipeline]] — 模块展开详细流程
- [[hierarchy-interfaces]] — IFSMNode / IFSMNodeFactory
