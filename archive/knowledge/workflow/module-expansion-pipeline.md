---
name: module-expansion-pipeline
description: FSM 模块展开的两 pass 编译流程、事件映射与命名规则
metadata:
  type: knowledge
---

# 模块展开管道

`BuildStateVisitor` `BuildTransitionVisitor` `FsmModuleInfo` `FsmModuleInstance` — 模块在编译期两 pass 展平到父 FSMEngine

## 背景 / 问题

FSM 模块系统需要将独立的模块定义（`.fsm` 文件）编译时展平到父 `FSMEngine`。关键挑战：模块内部节点/事件需加前缀命名、output 事件需映射到外部事件名、transition 占位符需替换为内部节点。

## 核心设计

### Pass 1 — BuildStateVisitor（节点/事件展开）

1. **`VisitImport_statement`** → 调用 `LoadModuleWithDependencies` 递归加载 `.fsm` 文件，解析模块内容，填充 `FsmModuleInfo.TemplateNodes/Events`
2. **`VisitDefState2` 检测模块类型** → 若 `state_type` 是已注册模块名 → 调用 `ExpandModuleInstance`
3. **`ExpandModuleInstance`** 四阶段：
   - Phase 1（node=null）: 解析实例 body，收集 output 映射到 `currentOutputMap`
   - Phase 2（node!=null）: 调用 `UpdateEventDescriptions()` 刷新模板 → `ApplyTemplateBranches` 创建带前缀节点 + output 替换 + 填充 `ExternalEventToInternalNodes`
   - Phase 3: 展开模板事件（output 事件按映射替换或加前缀）
   - Phase 4: 展开模板连线（内部 transition 加前缀，output 事件按映射替换）

### Pass 2 — BuildTransitionVisitor（连线展开）

`VisitDefTransition` 三段式逻辑：
- **Case 1**: fromNode 是模块实例名 → 从 `ExternalEventToInternalNodes` 解析内部节点 → 为每个内部节点创建 transition
- **Case 2**: toNode 是模块实例名 → 抛出 ScriptException（不能连线到模块实例，应连线到具体 Entry Point）
- **Case 3**: 正常情况，按原逻辑处理

### Fluent API 路径

`FSMDefineBuilder.ExpandModuleInstances` 执行与脚本路径相同的展开逻辑。**与脚本路径的关键差异**：Fluent API 通过 `FSMEngineBuilder.Build()` 触发展开，nodeDict 操作直接走 engine API。

## 关键规则

1. **前缀格式**：`{instanceName}.{nodeName}`，分隔符是 `.`（ANTLR STRING 规则需包含 `.`）
2. **output 映射优先级**：Fluent API `MapOutput` > 实例 body 索引映射。一个 output 事件只能映射到一个外部事件
3. **`UpdateEventDescriptions()` 必须在读取 `EventDescriptions` 前调用**——`SetBranchEvent` 只写 `branchDict`，不更新 `EventDescriptions`
4. **`moduleInstances` 生命周期**：`CreateStateMachine` 和 `Transform` 中均需清理（后者之前遗漏，已修复）
5. **`isParsingModuleDefinition` 必须 try/finally 保护**——模块体解析异常时不恢复会粘滞
6. **`RegisterModule` 幂等**：重复注册同一模块名静默跳过

## 交叉引用

- [Record — FSM 模块化系统](../../records/2026-05-31-fsm-module-system.md)
- [ADR-0001 — 编译期展平方案](../../../docs/adr/0001-module-system-compile-time-expansion.md)
- [CONTEXT — 模块系统术语](../../CONTEXT.md)
