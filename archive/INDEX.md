# Archive Index

## Records

- [2026-05-31-fsm-module-system](records/2026-05-31-fsm-module-system.md) `module` `import` `FSMEngine` `BuildStateVisitor` — 编译期展平模块系统：支持 module/import 语法和 Fluent API

## Knowledge

### engine/ — 引擎架构（3 文件）
- [core-architecture](knowledge/engine/core-architecture.md) — FSMEngine 整体架构、三种构建方式、数据流和核心字典
- [fluent-api](knowledge/engine/fluent-api.md) — FSMDefineBuilder/FSMEngineBuilder 构建器模式与模块配置
- [script-parsing](knowledge/engine/script-parsing.md) — 脚本两 pass 编译流程：BuildStateVisitor + BuildTransitionVisitor

### executor/ — 执行器（1 文件）
- [scheduling-lifecycle](knowledge/executor/scheduling-lifecycle.md) — FSMExecutor 事件驱动调度、Channel 队列、生命周期管理

### node/ — 节点系统（2 文件）
- [hierarchy-interfaces](knowledge/node/hierarchy-interfaces.md) — IFSMNode 节点继承体系、接口定义、Attribute 和工厂模式
- [composition](knowledge/node/composition.md) — GroupNode/ParallelNode 组合模式：子图嵌套与并行执行

### yield/ — Yield 指令（1 文件）
- [instruction-system](knowledge/yield/instruction-system.md) — IYieldAction 接口设计与全部 Yield 指令类型的语义

### script/ — 脚本语法（1 文件）
- [grammar-reference](knowledge/script/grammar-reference.md) — StateMachineScript.g4 完整文法参考

### workflow/ — 工作流（1 文件）
- [module-expansion-pipeline](knowledge/workflow/module-expansion-pipeline.md) — 模块展开的两 pass 编译流程、事件映射与命名规则
