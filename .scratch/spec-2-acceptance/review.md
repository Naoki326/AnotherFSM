# 父规格 #2：独立双轴审查

固定基线 `06322e367f3f032315d8975b7e1ce08dbd6bebc5`，验收目标 `4179c6a037f4645e689fbce1e8cb8cc9de18fee1`。
审查命令 `git diff 06322e3...4179c6a`，包含三个独立子票提交：`d9f3ba9`、`a5b530a`、`4179c6a`。

两轴均由独立 `code-reviewer` 会话只读审查，不将子票已关闭视为父规格当然通过；主代理负责执行验证、甄别证据。

## Standards

### 已核实

**硬违规：未发现有充分证据的文档化规范违规。**

**判断建议（heuristic，非硬违规）——Duplicated Code**

- 位置：`demos/FSMDemo.Frontend/src/stores/engineStore.ts:68–70、75–77`。
- 原文：两处重复相同的 `Promise.all` 请求列表：`api.getStatus(), api.getNodes(), api.getConnections(), api.getModuleInstances(), api.getModuleDefinitions()`。
- 基线依据：“重复逻辑→抽共享形态”。
- 建议：抽取私有图快照加载函数，供刷新与导入复用；保留导入路径一次性更新图及重置选择、日志的行为，避免以后扩展图数据时两处遗漏同步。

### 推测

无。

### 主代理核对

已直接阅读上述两处代码，确为相同图快照读取，但导入的状态重置合同不同。这是低优先级维护性建议，不是父规格验收阻塞。本次为只读产品验收，不顺手实施无关重构，保留建议。

## Spec

### 已核实

**WASM 展示适配漏项：词形前缀被错误当作模块归属。**

- **位置：**`demos/FSMDemo.Wasm/Services/DemoRuntime.cs:37,57,170–187`。
- **可复现路径：**`.scratch/spec-2-acceptance/wasm-probe/Program.cs` 经公开 `ImportScript/Nodes/Connections/EnterModule` 验证；审查员已读取主代理生成的 `.scratch/spec-2-acceptance/wasm-probe.log`：
  - Root 隐藏普通节点 `A.Observer`；
  - 普通连接被误投影为 `A --Observe--> End`；
  - 进入模块 A 又错误显示 `A.Observer`。
- **精确规格依据：**
  - 决策18：“导入、创建模块实例、接线及导出的既有演示路径应采用新接口语义”。
  - Testing14：“为迁移所必需的显示适配不改变普通节点接线行为。”
- **边界结论：**纳入父规格验收。问题涉及现有 WASM 导入后的实际节点、连接展示，不是要求重做编辑器或新增操作。应依据实际展开成员归属投影，而非名称前缀。

### 推测

无新增推测。`Events=[]` 未被 `Home.razor` 消费，不单独认定为用户可见缺陷。

### 主代理核对

已核对原文和源码，并运行链接真实消费者实现的可复现公开入口探针（exit 1）。阻塞缺口已记录到 [#6](https://github.com/Naoki326/AnotherFSM/issues/6)，#2 的原生 blocked-by 关系已远端回读确认指向 #6；本次不认领修复票。

## 汇总

Standards：0 硬违规，1 项非阻断维护性建议；Spec：1 项已核实验收阻塞（WASM 普通点号身份投影错误）。不跨轴重新排序，不将全套测试通过替代产品合同验收。
