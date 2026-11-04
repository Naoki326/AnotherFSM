# 工单管理：GitHub

工单和规格存放在 GitHub Issues，使用 `gh` CLI 操作。
仓库从 git remote 推断，当前为 Naoki326/AnotherFSM。

## 常用操作

- 创建：`gh issue create --title "..." --body "..."`
  多行正文使用 heredoc。
- 阅读：`gh issue view <number> --comments`；
  标签等结构化字段通过 `--json` 获取。
- 列表：`gh issue list --state open --json number,title,body,labels,comments`；
  按需增加 `--label`、`--state` 和 `--jq`。
- 评论：`gh issue comment <number> --body "..."`
- 标签：`gh issue edit <number> --add-label "..."` 或 `--remove-label "..."`
- 关闭：`gh issue close <number> --comment "..."`

技能要求“发布到工单系统”时，创建 GitHub Issue；
要求“读取相关工单”时，读取正文、标签和评论。

## PR 分诊入口

**PRs as a request surface: no.**

## 父子工单和阻塞关系

- 优先使用 GitHub 原生 sub-issues。
  gh 2.94+ 可使用 `gh issue create --parent <parent> ...`，
  或 `gh issue edit <parent> --add-sub-issue <child>`。
- 旧版 CLI 可调用：
  `gh api --method POST repos/{owner}/{repo}/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>`。
- 不支持原生父子关系时，在子工单顶部注明 `Part of #<parent>`，
  并在父工单任务列表中链接子工单。
- 阻塞关系优先使用原生 issue dependencies：
  `gh api --method POST repos/{owner}/{repo}/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`。
- 数据库 ID 通过
  `gh api repos/{owner}/{repo}/issues/<number> --jq .id` 获取，
  不使用工单编号或 node_id 代替。
- 不支持原生依赖时，在正文顶部记录 `Blocked by: #<number>`。
  所有阻塞工单关闭后才可开始。

## Wayfinder

- 地图工单使用 `wayfinder:map` 标签，正文维护 Notes、
  Decisions-so-far 和 Fog。
- 子工单链接地图，按类型标记 `wayfinder:research`、
  `wayfinder:prototype`、`wayfinder:grilling` 或 `wayfinder:task`。
- 从地图的未关闭子工单中，排除有负责人或仍被阻塞的工单，
  按地图顺序选择第一个。原生依赖通过
  `issue_dependencies_summary.blocked_by` 判断是否仍有未关闭的阻塞工单；
  使用正文回退时，逐一确认 `Blocked by` 中的工单状态。
- 认领：`gh issue edit <number> --add-assignee @me`，
  作为该工作会话的首次写操作。
- 完成后评论结论、关闭工单，并向地图的 Decisions-so-far
  添加摘要与链接。
