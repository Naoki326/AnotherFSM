# FSM 模块化系统

> **TL;DR**: 实现编译期展平模块系统，支持 `module/import` 语法和 Fluent API，6 项自动化测试覆盖，18 项 code review 发现中修复 15 项 `module` `import` `FSMEngine` `BuildStateVisitor` — 单文件状态机→可复用模块

## Background

AnotherFSM 原本是单文件扁平状态机模型：所有 Node/Event 存储在 `FSMEngine` 的两个扁平字典中。复杂流程的全部分定义挤在一个脚本文件里，无法拆分复用。`GroupNode`/`ParallelNode` 虽是子图机制，但引用的是同一个引擎内的节点，不是真正的模块边界。

需求：每个模块内部是部分状态机，可定义输入节点和输出事件，可被其他文件引入。

详见 [ADR-0001](../docs/adr/0001-module-system-compile-time-expansion.md)。

## Decisions

1. **编译期展平**：模块运行时扁平展开到父 `FSMEngine`，不创建独立引擎实例。复用现有 `def` 语法，模块作为"节点类型"使用
2. **前缀分隔符 `.`** ：模块实例内部节点命名为 `{实例名}.{节点名}`，ANTLR STRING 规则添加 `.` 字符支持
3. **output 事件映射**：实例 body 中 `0->GoEvent;`（索引）或通过 Fluent API `MapOutput("Done", "GoEvent")`（命名）
4. **两 pass 编译**：Pass 1（BuildStateVisitor）展开模块节点/事件，Pass 2（BuildTransitionVisitor）替换占位符
5. **RegisterModule 幂等**：重复注册同名模块静默跳过，避免跨构建异常
6. **Transform() 清理 moduleInstances**：轻量重新连线时清除旧模块实例数据

## Results

### 新增文件
- `StateMachine/Model/FsmModuleInfo.cs` — 模块定义模板
- `StateMachine/Model/FsmModuleInstance.cs` — 模块实例
- `StateMachine/Engine/FSMEngine_Module.cs` — 注册表+依赖加载+循环引用检测
- `FSMScriptAnalyzerTest/TestNodes.cs` — 测试用 Node 类型
- `FSMScriptAnalyzerTest/SubFlow.fsm` — 示例模块文件

### 修改文件
- `StateMachine/Engine/StateMachineScript.g4` — 新增 `module/import/input/output` 关键字，STRING 添加 `.`
- `StateMachine/Engine/BuildStateVisitor.cs` — 模块定义解析+实例展开+output 映射+try/finally 标志保护
- `StateMachine/Engine/BuildTransitionVisitor.cs` — 占位符替换+跨模块连线
- `StateMachine/Engine/FSMEngine_Script.cs` — `CurrentScriptDirectory`、`Transform()` 清理
- `StateMachine/Engine/FSMDefineBuilder_FluentApi.cs` — `IFSMModuleConfigurator`、`AddModule`、Fluent/Transform Builder 模块实例支持
- `StateMachine/Engine/FSMEngine_Function.cs` — 节点名校验（尾部 `.`）
- `FSMScriptAnalyzer/FSMScriptAnalyzerGenerator.cs` — `.fsm` 仅用于模块索引，正则支持点号
- ANTLR 自动生成文件（Parser/Lexer/Visitor/Listener）

### 测试覆盖
6 项自动化测试：模块注册+执行、多次实例化、Fluent API、错误路径（无效索引异常+幂等）、Transform 清理、重复注册

## Legacy

- **命名 output 映射不支持脚本语法**：ANTLR 无法为 `state_branch` 中 STRING 首 token 生成分支，`#OutputEventMapping` 已从语法中移除。脚本只能用索引映射（`0->GoEvent`），命名映射仅走 Fluent API
- **嵌套模块（模块 import 模块）**：当前仅支持单层，模块定义中的模块实例展开延迟处理待实现
- **源生成器重复模块名崩溃**：两个 `.fsm` 含同名 module 时 `RegisterModule` 幂等但构建可能异常，需源生成器重构
- `GroupNode`/`ParallelNode` 暂保留，后续评估是否可废弃
