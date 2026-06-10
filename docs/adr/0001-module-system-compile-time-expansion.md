# ADR-0001: 模块系统采用编译期展平方案

- **日期**: 2026-05-29
- **状态**: 已决策
- **决策者**: naokiz

## 背景

AnotherFSM 当前是单文件扁平状态机模型：所有 Node 和 Event 存储在 `FSMEngine` 的两个扁平字典中。当构建复杂流程时，所有定义挤在一个脚本文件里，无法拆分复用。目标是引入"模块化"能力，允许将部分状态机封装为独立模块，被其他文件引入使用。

## 决策

模块系统采用**编译期展平（Compile-time Expansion）**方案：

1. **模块是纯编译期概念**——展开后所有 Node/Event 以 `实例名.原名` 的 key 存入父 `FSMEngine` 的扁平字典，运行时无感知
2. **模块定义接口**：顶部声明 `input`（入口节点名列表）和 `output`（出口事件名列表）
3. **引入语法**：`import SubFlow` + `def Sub1(SubFlow) { Done->GoEvent; ... }`，复用现有 `def` 语法，模块作为"节点类型"使用
4. **事件映射**：实例 body 中 `OutputEvent->ExternalEvent`，编译期将内部节点的 branch 改绑为外部事件名
5. **Transition 占位符**：外部转移中的源节点写模块实例名，编译期替换为实际发出事件的内部节点
6. **循环引用**：编译期检测并报错
7. **可视化**：模块实例生成一个轻量视觉代理节点（含 Pos/Color），但不存入 nodeDict，不参与执行
8. **模块文件发现**：文件系统查找，相对路径基于当前脚本文件目录

## 替代方案与取舍

| 方面 | 选中的方案 | 替代方案 |
|------|-----------|---------|
| 运行时模型 | 扁平展开到父 `FSMEngine` | 独立 `FSMEngine` 实例 + 桥接（类似 GroupNode） |
| 目标 | 拆分复杂度（脚本文本级复用），不新增能力 | 运行时隔离边界（独立引擎），改动执行模型 |
| 重用 | 同一模块可多次实例化（不同前缀） | 仅单例引入 |

**放弃独立引擎方案的原因**：
- 现有的 `FSMExecutor`、`GroupNode`、`ParallelNode` 都依赖"所有 Node 在同一 `FSMEngine`"的假设
- 独立引擎需要桥接机制，引入 `FSMExecutor` 间通信的复杂性
- 用户的核心需求是拆分复杂度，而非运行时隔离

## 影响

- 需要修改 ANTLR 语法（`module`、`import`、`input`/`output` 声明）
- 需要修改 `BuildStateVisitor` / `BuildTransitionVisitor` 支持两 pass 编译
- 需要新增 `ModuleRegistry` 在 `FSMEngine` 上
- 需要扩展 Fluent API（`IFSMDefineBuilder` 新增 `AddModule` 等方法）
- `namespace` 语法可由 `module` 替代
- `GroupNode`/`ParallelNode` 暂时保留不动，后续评估是否可以废弃
