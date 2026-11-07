# #34 双轴独立审查与逐项处置

固定基线：`bfa3fc7f5d4580549e7a4b62bfed47c84bd87f4b`。首轮固定 `336415b`；最终全量固定 `4f16a36`，分别增量复核 `4f16a36..5a8f311`、`5a8f311..c1ffd56`。两轴使用独立 reviewer 会话，均只读，未把静态审查冒充测试执行。

## Standards

**最终文档化硬违规：0；新增阻断项：0。**

1. 首轮低优先 heuristic：`BridgeQueries`/`NativeSearch` 重复消费项目/TFM 标签。主代理对照原文确认，采纳，统一 `BindingSettings.HostLabel`；最终全量未再报此项。
2. 最终全量低置信、非阻塞 **Duplicated Code**：`ComplexHostAcceptance.enter` 与 `ComplexBoundaryAcceptance.gesture` 重复 Swing Enter 的 InputMap/ActionMap 及 KeyListener 查找。
   - 主代理逐行核实：确有重复，但 Complex 为选中 JList 的按下/释放，Boundary 为通用 JComponent/JList/JTree、按下及双击，且各自日志/断言不同。
   - **接受为测试内低优先技术债，暂不提取**，避免验收末尾引入跨旅程手势耦合；不是硬规范违反，也未写成已经消除。增量 reviewer 确认无新增可行动项。

最终增量核实：实际 C# 文本 SHA256 判定业务输入；SDK stamp 仅控制派生目录缓存的复用/回填，不直接推进结果 epoch 或取消操作；真实结构变化仍立即失效。未发现文档规则冲突。

## Spec

**最终未解决产品缺陷：0；有据越界：0；未解决本票功能缺项：0。**

- 首轮 **P2：单宿主显式覆盖成功导航吞掉配置来源**。主代理核对 `NodeBindings`、`BridgeQueries`、`SemanticUi` 确认；修复保留声明文件规则与来源到成功导航通知。真实红例 `override-source-ui-red.json`；完整 UI 174 项中的显式映射来源断言已绿。
- 最终全量最初记录 AC11 的矩阵/生命周期实证未齐，明确为验收 partial 而非 API 缺陷。主代理补齐 28 组合 SDK/174 项实际 UI、真实三类重载/卸载加载、独立来源策略 12 项和真实 Don't Open 及局部 DSL。
- 独立 Spec reviewer 回读后撤回 AC11 缺口：不能要求每个有意分离 scope 的旅程顶层状态都为 passed；生命周期报告 58–61 的原始 policy pending 保留，由独立来源策略 suite 的真实有效策略/导航和 exact-settings 还原 receipt 覆盖，不涂改原始报告。
- `5a8f311..c1ffd56` 再审确认 AC10 真实项目/引用/配置/宿主/TFM 失效仍成立，缓存新鲜度与业务输入版本分离未破坏旧结果拒跳。

## 实测调查与证据分级

- 真实 SDK 红绿：alias-only 假契约污染 global 约定、net8 条件项目项误入 net10、早期不完整 NuGet 目录缓存；均有原始失败及修复后正式 SDK/UI。
- 真实 UI 红绿：调用者配置泄漏导入 Input 的声明文件、缓存/PSI 构建让新结果自过期。最终按实际声明 DocumentId 和实际源文本变化判断。
- SDK `CompilationContextCookie.GetOrCreate` 会正确压栈切换上下文，主代理排除“保留错误 cookie”假说，未据推测改代码。
- 公开 `IPsiFiles.PsiCacheTimestamp` 的 int 签名已由主代理反编译核验；只版本化目录，不冒充业务变更证明。
- 方法级反编译失败有匹配 MVID 的宿主生成文本 `ISSUE: unable to decompile the method.`，不扩大为整个引擎异常/取消。端点离线/401不扩大为整机断网或真实私库凭据访问。

汇总：Standards 0 硬违规，1 项低优先测试重复显式接受；Spec 首轮 P2 已修复且实测、最终缺陷/功能缺项 0。#35 编写辅助与 #36 最终性能不作为本票缺失，也不提前宣称通过。远端 push、关闭与回读由交付台账单独证明。
