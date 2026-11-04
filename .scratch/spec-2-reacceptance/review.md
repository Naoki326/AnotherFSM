# #2 最终双轴审查

## 范围

- 固定整体基线 `06322e367f3f032315d8975b7e1ce08dbd6bebc5`，到 `93160043fded1c4b0136f7c29f40a0ce35a140d0`，另加本窗口工作区完整补丁。
- 子票实现：d9f3ba9 (#3)、a5b530a (#4)、4179c6a (#5)、9316004 (#6)；d01f429 为首轮验收历史。
- 规格：`../spec-2-acceptance/spec.md`，此次评估整个父规格，不把其他已完成子票排除。
- 两名独立 `code-reviewer` 只读审查；主代理负责逐条核对文件、行为及复现，不把推测自动当成缺陷。

## Standards

完整合并 diff 未发现文档化规范硬违规。两项低优先级启发式 Duplicated Code 建议（不是阻断、不是硬违规）：

1. `demos/FSMDemo.Frontend/src/stores/engineStore.ts:68–77`，刷新与导入重复同一五接口 Promise.all；可后续抽图快照读取函数，保持导入原子状态更新。
2. `StateMachine/Engine/BuildStateVisitor.cs:183–186` 与 `StateMachine/ScriptAnalyzer/NodeListener.cs:23–26`，use 属性白名单与诊断重复；可后续共用内部校验函数。

主代理已核对上述代码属实，不为关闭父规格顺手引入无关整理。新增 WASM／React／分析器补丁审查未发现规范硬违规或可行动的新增 smell。推测：无。

## Spec

审查及浏览器复验发现的实际问题均经主代理验证：

1. WASM Razor 活动高亮按前缀推断归属。真实 DOM 红灯：A.Observer 活动时模块 A 也 active。改为实际成员的 Nodes.IsActive 投影，两条服务执行回归和相同 UI 断言绿灯。
2. React 活动节点、转移及同步仍按前缀折叠。真实 DOM 红灯：模块 A 动画、普通 A.Observer 无动画；修复后普通节点和 Start→A.Observer 转移均正确，真实 Retry 的 Root／模块内表现保留。
3. ReadScript 最长前缀把 Check 的公开 Sub.Done 归给 Check.Sub。合法 Engine 构建成功、ReadScript 回调断言红灯；同文件公开接口元数据复用核心 FindPublicOwner 后绿灯。
4. 局部 N.Area 遮蔽真实限定出口 Area.Check.Done。合法 Engine 成功、ReadScript 抛错红灯；以真实局部／限定公开身份决定后绿灯。

补丁保留声明投影边界，缺少外部模块元数据且有多个未知候选时明确报歧义，不猜测，不新增文件加载或测试专用接口。无已核实范围扩张；保留历史公共字段和未消费 Events=[] 不算缺陷。

最后一项限定出口补丁已独立复核：局部／完整限定公开接口优先于语法投影回退，Engine 与 ReadScript 一致性、外部未知多候选诊断均有测试。最终 Spec 未解决已核实发现：**0**；推测：无。实际浏览器与测试结果见 [ACCEPTANCE.md](ACCEPTANCE.md)。

总结：Standards 0 硬违规、2 项非阻断重复代码建议；Spec 的已核实功能缺口全部修复，未解决 0。两轴分别评价，不用其中一轴的通过掩盖另一轴的问题。
