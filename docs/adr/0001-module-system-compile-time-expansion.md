# ADR-0001: 模块系统采用编译期展平方案

- **日期**: 2026-05-29
- **状态**: 已决策
- **决策者**: naokiz

## 背景

AnotherFSM 当前是单文件扁平状态机模型：所有 Node 和 Event 存储在 `FSMEngine` 的两个扁平字典中。当构建复杂流程时，所有定义挤在一个脚本文件里，无法拆分复用。目标是引入"模块化"能力，允许将部分状态机封装为独立模块，被其他文件引入使用。

## 决策

模块系统采用**编译期展平（Compile-time Expansion）**方案：

1. **模块是纯编译期概念**——展开后所有 Node/Event 以 `实例名.原名` 的 key 存入父 `FSMEngine` 的扁平字典，运行时无感知
2. **模块定义接口**：顶部声明 `input`（公开入口节点名列表）和 `output`（公开出口事件名列表）；Output 是事件，不是终止节点，不引入 `terminal`
3. **实例化语法**：使用 `use Checker as CheckA;`；可选 body 仅允许 `Pos`、`Color`、`FlowID` 元数据，例如 `use Checker as CheckA { Pos:(1,2); Color:"red"; FlowID:id; }`。模块实例不是节点类型，不使用旧模块 `def`，也不在 body 中配置出口映射
4. **入口连接**：普通事件转移保留 `Launch -> Start to CheckA;`，其中 `Launch` 是事件、`Start` 是源节点、`CheckA` 是目标模块实例；解析保留普通事件优先。目标模块只有一个 Input 时可省略入口名，多 Input 时必须用 `CheckA.入口名` 选择公开 Input，不得直接访问未公开的内部节点
5. **出口连接**：直接使用限定出口事件，例如 `CheckA.OK -> Next;`、`CheckA.OK -> CheckB;` 或 `CheckA.OK -> CheckB.Retry;`。`Next` 是普通目标节点，`CheckB` 是单入口模块实例，`Retry` 必须是 `CheckB` 的公开 Input；多入口必须显式选择。禁止名称或数字形式的输出映射，也禁止在外部事件转移中把模块实例当作占位源节点
6. **出口发布者**：编译器从模块内部真实 Branch 的事件绑定确定 Output 的发布节点；有多个发布者时全部连接，同一节点的多个 Branch 发布同一 Output 时按节点去重。外部连线不改写 Branch 的事件绑定，不通过事件重命名来桥接外部流程
7. **事件隔离**：Output 以实例限定名（如 `CheckA.OK`）接入父流程，保留不同实例之间的事件隔离；不额外发布裸名 `OK` 作为全局出口
8. **循环引用**：编译期检测并报错
9. **可视化**：模块实例生成一个轻量视觉代理节点，承载 `Pos`、`Color`、`FlowID` 元数据，但不存入 nodeDict，不参与执行
10. **模块文件发现**：文件系统查找，相对路径基于当前脚本文件目录
11. **Namespace 独立**：Namespace 仍用于以统一前缀组织名称；Module 用于声明接口和复用流程，两者不能互相替代，也都不提供独立运行时引擎

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

## 有意的破坏性变更

本决策修订并取代旧模块 `def`、实例 body 出口映射及外部事件的实例占位源连线方案，不提供这些旧模块 DSL 的兼容写法。驱动因素是语法审美、接口归属和唯一规范表达，而非运行时能力扩张：

- `use … as …` 明确表达模块复用，避免将模块伪装成节点定义；body 只承载元数据。
- Input/Output 归模块定义所有；外部流程选择公开接口并连接目标，不在每个实例上重定义出口。
- 限定出口事件直接连线是新 DSL 的唯一规范表达，避免名称映射、数字映射和占位源形成多套等价写法。

此边界仅针对模块 DSL。独立 C# Fluent API、现有消费者依赖的旧公共字段不作全面清理；历史或独立程序 API 中的 Event Mapping 不代表新 DSL 必须执行映射步骤。

## 影响

- 需要调整 ANTLR 语法以支持 `use … as …`、仅含三种元数据的实例 body 及公开 Input/Output 连接，拒绝上述旧模块 DSL
- `BuildStateVisitor` / `BuildTransitionVisitor` 继续采用两 pass 编译，负责接口解析、真实发布者收集、去重及限定事件连接，不因外部连线改写 Branch
- `FSMEngine` 上的 `ModuleRegistry` 承载编译期模块信息，运行时仍执行展平后的节点与事件
- 独立 C# Fluent API 保留其程序化构建职责，不要求与新 DSL 的语法变更同步清理所有旧接口或消费者字段
- Namespace 保持独立的名称组织含义，不由 Module 替代
- 后续决策 [#14](https://github.com/Naoki326/AnotherFSM/issues/14#issuecomment-6647973766) 已取代暂留安排；[#17](https://github.com/Naoki326/AnotherFSM/issues/17) 删除 GroupNode/ParallelNode 六种形态及运行时组合入口。普通 Module 只承接定义复用，不等价替代父子生命周期、包装出口或并行汇合，不新增 ParallelModule。
- 本次只更新架构决策与领域术语；使用指南、生成示例与编辑器文档另行处理
