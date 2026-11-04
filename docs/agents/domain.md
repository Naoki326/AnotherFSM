# 领域文档

## 探索代码前

本仓库采用单上下文布局：根目录 `GLOSSARY.md` 和 `docs/adr/`。

- 阅读根目录 `GLOSSARY.md`。
- 阅读 `docs/adr/` 中与当前任务相关的 ADR。

目标文件不存在时继续工作，不提示缺失，也不主动建议创建。
`/domain-modeling`（可由 `/grill-with-docs` 和
`/improve-codebase-architecture` 调用）在术语或决策确定后按需维护这些文档。

## 使用领域词汇

工单标题、设计建议、假设和测试名称使用 `GLOSSARY.md` 中定义的术语，
避免替换成词汇表明确排除的同义词。

遇到未定义概念时，先检查是否误用了项目术语；
确有缺口则记录，供 `/domain-modeling` 后续处理。

## ADR 冲突

建议与现有 ADR 冲突时，明确指出对应 ADR、冲突内容及重新讨论的理由，
不默默覆盖已有决策。
