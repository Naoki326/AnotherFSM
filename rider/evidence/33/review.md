# #33 双轴审查与处置

基线：`1ef8913`。首轮独立审查固定 `99204cb`，主代理逐条核对原文与代码，不把后续修复追溯为初轮无发现。

## Standards

首轮：**硬规范违反 0；低优先级、非阻塞 heuristic 1**。

- **Duplicated Code**：`ScriptDocuments.Import` 与 `Local` 重复实体目录/名称校验及 `M.fsm` 拼接。
- 主代理核验：两处确实相同，采纳。`ad7c331` 提取 `ResolveImportPath`，项目文档与局部文档加载及归属策略仍各自保留。SDK 编译 0 warning / 0 error。
- 独立复核 `99204cb..ad7c331` 确认重复已消除、加载/归属职责未混合；最终 `ad7c331..bf22f95` 再审新增硬违反 0、heuristic 0。原建议不被写成硬规范缺陷。

## Spec

首轮：**3 项 P2；无有据越界项；无推测项**。独立 reviewer 已复核 `99204cb..4c0eb05`，确认三项全部 resolved，无新增有据缺陷。

| 发现 | 主代理核验与修复 | 回归 |
| --- | --- | --- |
| S1 整实例“模板全部引用”仍局限自身 | AC3 确实要求可切模板；`ea2f202` 在 whole Instance 的 template 模式切到 Module，成员 proxy 保持原语义 | `instance-template-red.log` 的预期两处 Module 使用/实际一处实例位置，随后 green；新宿主 whole-instance 查询期望 5 处 |
| S2 损坏多 Input 被过滤成单 Input | AC4/AC10；`4c0eb05` 统计所有已声明 Input，再验证唯一项可靠，Missing 不授权省略入口 | `damaged-input-red.log` 复现错误 resolved，随后 green；宿主省略连接/入口操作双拒绝 |
| S3 别名链只解引用一层 | 运行时注册表允许链式别名；`4c0eb05` 将 alias 的模板目标归一到真实 Module，别名声明/查询身份仍独立 | `alias-chain-red.log` 复现错误文件落点，随后 green；默认 B、本体 Module、模板 3 引用及局部别名 1 使用分别验证 |

相关公开核心测试：`tests/AnotherFSM.Semantics.Tests/InterfaceReferenceFacts.cs`。以上日志在 `.scratch/issue-33/regression/`；主代理独立运行核心全部 63 项及 .NET solution 498 项通过，不将 reviewer 的日志回读冒称其独立执行。

## 聚焦 SDK 调查及设计复核

前期 investigator 提出的原生受限域泄漏、路径文档误作项目缓冲区已独立核对并修复，详见 [preintegration-review.md](preintegration-review.md)。永久 visitedModules 已删除；抢占恢复未人为注入，不能声称已复现。

import-only 反查先由真实运行排除语言/单源词过滤，确认 broad scope 合并/narrowing 漏掉 source；采用公开 `ITwoPhaseDomainSpecificSearcher` 后，显式及 solution Finder 均命中真实 `83/8 GraphKey`，受限 file A 仍只有本文件两个键。没有新增 PSI module、伪造项目项、另建 workspace 或恢复域外结果提交。

Designer 独立回读目标 SDK 及官方 C# 同接口实现，确认二阶段路线成立；另提醒 SDK `processedFiles` 会混合整文件及节点访问，不能直接当完整处理证明。主代理按该静态契约加固为仅记录本 adapter 成功完成的整文件回调、节点不升级覆盖、交集前后/提交前同 epoch 检查，并增加公开 SDK 回归。此项是契约加固，不伪称已在用户 UI 复现节点遗漏。最终 `bf22f95` ZIP 的 SDK 81 项、真实 DSL 117 项、原 51 阶段及 socket 恢复均通过；Spec 最终复核 `4c0eb05..bf22f95`、Standards 最终复核 `ad7c331..bf22f95` 均无新增发现。实际 `ProcessElement` 子树域与人为取消竞态仍只是代码/SDK 契约核验，未声称有该语言真实 PSI 节点交互证据。

## 完成门槛

Standards：硬规范违反 0；初轮低优先 heuristic 1 已修复并复核，未解决 0。Spec：初轮 P2 发现 3 全部修复并复核，未解决 0。两轴分别保留，不合并排序；Standards 原最重项为非阻塞重复，Spec 原最重项为 P2 语义边界错误。

最终实现提交 `bf22f95` 的功能/检查/审查/可重复 ZIP 和安装回读已通过。远端交付必须另以正常 push、工单最终证据/关闭及远端回读确认，本审查不代替交付状态证明。#34–#36 不作为本票缺失，也不提前宣称通过。
