# Wayfinder 回读校验

完成决策工单的写操作后，运行只读校验工具；不是用返回的成功 URL 代替回读。

首次使用或锁文件更新后，先执行 `npm ci --prefix tools --ignore-scripts` 安装锁定的 Markdown 解析依赖。工具按语法树核验实际命名链接，不把注释、代码块或转义文本算作地图索引。

## 准备预期

1. 发布评论前，将最终 resolution 正文保存在本次工作目录的 UTF-8 文件中；例如 `.scratch/wayfinder/<ticket>/resolution.md`。这是校验输入，详细结论的权威位置仍为工单评论。
2. 用同一文件发布评论，避免终端转义改变文本：`gh issue comment <ticket> --body-file <file>`。
3. 保存返回评论 URL 中的数字 `issuecomment-<id>`，完成关票、地图索引与依赖变更。
4. 创建本次预期清单，关系数组必须明确列出全部预期关系，空关系填 `[]`。编号限定在 `repo` 指定的仓库，实际依赖按完整 URL 校验；跨仓库关系不会因编号相同而通过。不要从待校验的远端结果生成预期正文或依赖清单，这会变成自我比较。

示例 `expectation.json`（替换为本次值）：

```json
{
  "repo": "Naoki326/AnotherFSM",
  "map": 8,
  "ticket": 10,
  "comment": 6644350878,
  "bodyFile": "resolution.md",
  "blockedBy": [9],
  "blocking": [11, 12, 14]
}
```

`bodyFile` 相对预期清单所在目录解析。正文比较只统一 CRLF/LF，不忽略其他空白差异。

## 执行与完成标准

```bash
node tools/verify-wayfinder.mjs --expect .scratch/wayfinder/10/expectation.json
```

工具只执行 GitHub GET 请求，使用现有 `gh` 登录，每请求超时 20 秒；代理的 bash 调用也必须带超时（建议 60 秒）。

- **退出码 0**：正文一致、评论属于目标工单、工单以 completed 原因关闭、地图的 `Decisions so far` 有命名的评论链接、原生父子关系及双向依赖均匹配。
- **退出码 1**：事实不符合预期。只输出失败项、关系差异或正文首处差异（每侧最多 240 字符），不展开整个正文。
- **退出码 2**：输入、权限、网络或 API 响应失败；不能把失败当成空列表或验证通过。

失败后先确认远端实际状态，只重试未完成的步骤，不重复发布评论或创建工单。并发会话可能合法修改依赖；核对变更原因后才能更新预期清单，不能为了让检查通过而直接采用远端值。

此工具验证单个已完成决策工单，不代替地图前沿查询，也不证明地图已整体完成。未使用原生关系的旧工单需按 `issue-tracker.md` 的回退约定人工核验，不能假称本工具已覆盖。

## 离线回归测试

```bash
node --test tools/test/*.test.mjs
```

测试不调用 GitHub、不需要凭据，覆盖正文差异、错误评论归属、未关闭状态、索引缺失和依赖变化。
