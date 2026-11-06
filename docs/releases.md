# NuGet 统一版本发布

三个包随同一个 Git tag 一起发布，发布版本由 tag 决定，不需要逐个修改 `.csproj` 中的开发默认版本：

- `Naoki.AnotherFSM`
- `Naoki.AnotherFSM.ScriptAnalyzer`
- `Naoki.AnotherFSM.NodeAnalyzer`

## 首次配置

使用 NuGet **Trusted Publishing（OIDC）**，无需创建或保存长期 `NUGET_API_KEY` secret。

1. 在 NuGet.org 创建 GitHub Actions 的 Trusted Publishing 策略：

   | 字段 | 值 |
   | --- | --- |
   | Policy Name | `AnotherFSM GitHub Actions`（可自定义） |
   | Package Owner | `NaokiZ`（NuGet 包所有者，不是 GitHub 仓库所有者） |
   | Repository Owner | `Naoki326` |
   | Repository | `AnotherFSM` |
   | Workflow File | `release.yml`（仅文件名） |
   | Environment | 留空（当前工作流未配置 GitHub Environment） |
   | Glob Patterns and Packages | `Naoki.AnotherFSM*`；若确需授权该所有者下所有包，可填 `*` |

   多个包名或匹配规则每行一个。包名规则只控制授权范围，不会自动增加打包项目；本工作流仍只发布上述三个包。其他仓库或工作流需创建对应策略。
2. `.github/workflows/release.yml` 的 `publish` job 已授予 `id-token: write`，并使用 [`NuGet/login@v1`](https://github.com/NuGet/login) 换取短期发布 key。`user: NaokiZ` 是 NuGet 用户名；更换发布账号时需同步修改，不能填写 GitHub 用户名。
3. 将工作流及相关脚本提交、推送并合并到待发布提交。GitHub Release 使用自动提供的 `GITHUB_TOKEN`，无需额外 token。若之前配置过长期 `NUGET_API_KEY` secret，新流程不再读取它，可在确认无其他工作流使用后删除。

仅提交工作流不会发布；推送匹配的 tag 才会触发。现有 `master` → GitHub Pages 部署保持不变。

## 发布

先确认目标提交已通过 CI，且版本号没有在任何一个包中使用过。示例版本号请按实际变更选择；破坏性 API 变更应提升主版本。

```bash
git switch master
git pull --ff-only
# 确认 HEAD 就是要发布的提交，再创建并推送 tag。
git tag -a v2.0.0 -m "Release 2.0.0"
git push origin v2.0.0
```

预发布使用 `v2.0.0-beta.1`、`v2.0.0-rc.1` 等格式。工作流只接受 `v主版本.次版本.补丁版本[-预发布标识]`；数字标识不允许前导零，不接受第四段版本或 `+build` 元数据（避免 NuGet 版本归一化后碰撞）。

`.github/workflows/release.yml` 会依次：

1. 校验 tag，并提取统一版本。
2. 以 Release 配置和该版本运行 .NET 解决方案测试，以及两个生成器的可执行消费者回归。
3. 打包三个项目，并验证包集合、包 ID、实际 `.nuspec` 版本及核心/分析器 DLL 路径；全部通过才上传工作流 artifact。
4. 下载 artifact，通过 GitHub OIDC 和 NuGet Trusted Publishing 策略换取短期 key，发布三个包到 NuGet.org，再创建 GitHub Release，附上 `.nupkg` 和自动生成的说明。预发布 tag 会标记为 GitHub prerelease。

发布门禁针对 NuGet 库及生成器，不重复运行前端/真实 WASM 部署测试；这些仍由现有 CI/Pages 工作流负责。

## 检查与失败重试

- 在 GitHub **Actions → Publish NuGet packages** 查看运行结果和三个包的 artifact；成功后确认 NuGet.org 三个包的版本与 GitHub Release 附件。
- OIDC 登录或包授权失败时，核对 NuGet 策略中的仓库、工作流文件名、Environment、包名规则和 Package Owner，并确认 `user: NaokiZ` 对应正确 NuGet 用户。修复服务端策略后，在原运行中 **Re-run failed jobs**，会重新换取短期 key，无需删除或重新推送 tag。若错误在工作流代码本身，修复后使用新的发布 tag，不要移动已有发布 tag。
- NuGet 不提供跨包事务，可能出现部分包先发布成功；`--skip-duplicate` 允许重试时跳过已发布版本，继续剩余包。GitHub Release 只在 NuGet 推送步骤成功后创建；同一 tag 重跑时复用 Release。
- **发布过的版本不可覆盖。不要移动发布 tag 或用相同版本发布不同内容。** 构建或代码有误时，修复后使用新版本；不要把“跳过重复包”当作内容一致性校验。
- 推送非法 `v*` tag 会在版本校验阶段失败，不会发包。普通分支推送不会触发 NuGet 发布。

本地可运行无网络校验（需 Python 3）：

```bash
python3 -m unittest discover -s tools/test -p 'test_release.py' -v
python3 tools/release.py version v2.0.0-beta.1
# 对已经打好的三个包进行检查：
python3 tools/release.py verify v2.0.0-beta.1 artifacts/nuget
```

Windows Git Bash 可用 `python` 替代 `python3`。本地校验脚本不会上传包、创建 tag 或修改远端。
