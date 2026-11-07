# Retro 1/2/3 冻结审查与回归

审查固定点：`45e7ad1...849de5e`，包括 runner、独立 case/准备缓存、结构化诊断三次提交。仅 retro 范围；不是 #35，也不重开 #34。

## Standards

独立审查员 `01a1b5c2-0893-7598-bbcf-a5a81ef7c3ad`：2 项（硬性违规 1、判断性建议 1）。主代理已对照原文逐条甄别。

1. **采纳并修复：select 固定写入路径边界。** 原 `acceptance.py` 把 select 和只读 check 一并豁免，允许 `select --sandbox PATH` 写其它合法准备目录。与工具 README 的固定 `rider/.sandbox` 约束冲突。修复仅让 check 接受覆盖；公开 CLI 红测试证明旧版实际返回 selected/0，修复后拒绝/2，manifest/JAR 字节不变。完整 Python 回归 47 项、1 skip；另在真实固定沙盒成功 select index-invalidation → check → select all → check，恢复原选择，没有启动宿主/重新构建。原 Standards 审查员已只读复核 `849de5e..8df7992`，确认写入前拒绝且测试仍使用真实公开 CLI，本项关闭；主代理对照代码再次核验。
2. **暂缓：原子 JSON 写入重复（heuristic）。** contract 与 session 均有 UUID 临时文件/replace/finally unlink，session 另有 Windows PermissionError 有界重试。确认重复，但没有已观察功能失败；本轮不为消除 smell 额外重构写入模块，后续可靠性修复应注意两处策略。它不是文档化标准硬违规。

## Spec

独立审查员 `01a1b5c2-4793-7598-bbcf-a5ab6a916853`：1 项 P2，外部 MSBuild 环境输入闭包。主代理核对 SDK 的 `Microsoft.Common.props` 确认：DirectoryBuildPropsPath 和 CustomBefore/After 等可导入扫描树外的文件，原缓存只 hash 环境字符串，路径不变而内容改变可能误复用；这是已核实的条件性静态缺陷，不声称当前宿主曾误命中。

**采纳并最小修复**：闭包扫描发现相关环境 override family 即标 incomplete，保持实际编译环境，不尝试扩大为通用 MSBuild 求值器。6 种真实环境变量（含大小写变体）指向真实外部文件，先证实旧版在内容改变后仍 cached 的红，再验证全程保守 executed。完整 Python 回归现在 48 项、1 skip；普通无 override 的复用契约继续通过。原 Spec 审查员已只读复核 `6b63e12`，确认 incomplete 优先阻止复用、实际编译环境不变，本项关闭；主代理再次对照代码及测试核验。

## 冻结后完整回归：未通过，保留阻塞

- 原始报告：`.scratch/rider-acceptance/run-4e7cd7999863483aa788dc071df03d37/result.json`。
- runId `328b4ae2-5ef5-4c4a-bd4b-bf8ddc521805`，相同产品 SHA `c9effbb115283137727c6131c59c1e306769f5bb8e123a9c5913c3d8ef6e6159`。
- Complex all 状态 **assertionFailed**。174 个 UI 步骤中 9 passed / 1 failed / 164 notRun；第 10 步 `Web SDK static library` 超时。
- 新 UI journal 显示该请求 `83beb597-e4dd-4b7c-be4d-080fccf994a5`：RESOLVE reply accepted（epoch 9）→ NAVIGATE_TYPE submitted → CONTEXT_INVALIDATED（current epoch 10）。这是实际取消阶段/原因，不等于已确认触发 epoch 变化的根因。后端 bounded journal 后续采样已覆盖该时点，不能靠最终快照倒推来源。
- full SDK reporter 同 run/backend，**183/183 passed**；不等于完整 UI 旅程通过。
- 原有 project/reference/TFM reload、external source policy、decompiler policy/failure distinctions 等范围外 pending 保持原样，没有抹成通过。
- 按失败即停边界，没有重跑整个 Complex，也未继续 Legacy。该失败仍是最终验收阻塞，不宣称 retro 整体最终验收完成；新独立诊断 case 的成功仍只声明自身。
- 主代理独立核验原 frontend/backend 已退出；cleanup/resource errors 为空，原设置 1469 字节及全部 69 项夹具 hash 匹配。未 kill、未接受 Trust/Legal、未 push。

## 完整离线/仓库检查

独立代理在原冻结 `849de5e` 执行，2026-11-07 11:27:42–11:28:44 UTC，原始日志 `.scratch/rider-acceptance/final-offline/`。主代理已核对命令、退出码、原始失败及成功摘要。

| 检查 | 原冻结结果 |
| --- | --- |
| Rider Python | 47 项：44 passed / 2 failed / 1 skip |
| 后端 diagnostics | 13 passed |
| Release Python | 6 passed |
| .NET solution | 4 程序集，507 passed |
| tools Node | 22 passed |
| 前端 lint / typecheck | 均退出 0 |
| 前端 test | 12 passed |

原冻结 Python 两项失败：`test_propagated_interrupt_writes_cleanup_receipt` 中 hostsStopped=false；`test_stopped_heartbeat_is_distinct_from_startup_timeout` 得到 notStarted 而非 heartbeatStopped。原始失败未覆盖，根因未核实，不能认定为并行负载所致。

之后主代理分别在两处审查修复后完整运行 Python：`green-select-boundary.log` 为 47 项、1 skip；最新 `green-external-msbuild-inputs.log` 为 48 项、1 skip，均无失败。两处修复没有改动 session timing 测试，后续绿色不等于已定位/修复最初两项间歇失败；该稳定性风险保留。

检查只生成既有 bin/obj 中 245 项构建产物更新（含 3 个 nupkg）及 2 个 tsbuildinfo 缓存时间更新，没有源码漂移/依赖安装。原冻结后的两处功能修复仅涉及 runner CLI/缓存扫描，均已有对应红→绿全 Python 回归；没有据此重跑 UI 或宣称原完整 UI 阻塞消失。

## 收尾状态

本轮实现及审查修复均本地提交；没有 push、发布、重开 #34 或开展 #35。所有代理已完成，不继续启动长回归。当前自有 Rider frontend/backend 已退出、原设置/夹具核验一致；保持 Complex all 取证选择。

整体仍**未通过最终验收**：完整 UI 回归失败、初次 Python 两项失败的稳定性根因未明、Legacy 长回归未运行。后续若继续，应先就这些明确阻塞开展聚焦诊断，而不是把后续绿色/SDK passed 外推为整体验收成功。

双轴结论：Standards 共 2 项，最重为 P2 固定写入边界（已关闭），另 1 项重复写入逻辑 heuristic 暂缓；Spec 共 1 项，最重为 P2 外部输入缓存闭包（已关闭）。
