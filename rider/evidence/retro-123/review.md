# Retro 1/2/3 冻结审查与回归

审查固定点：`45e7ad1...849de5e`，包括 runner、独立 case/准备缓存、结构化诊断三次提交。仅 retro 范围；不是 #35，也不重开 #34。

## Standards

独立审查员 `01a1b5c2-0893-7598-bbcf-a5a81ef7c3ad`：2 项（硬性违规 1、判断性建议 1）。主代理已对照原文逐条甄别。

1. **采纳并修复：select 固定写入路径边界。** 原 `acceptance.py` 把 select 和只读 check 一并豁免，允许 `select --sandbox PATH` 写其它合法准备目录。与工具 README 的固定 `rider/.sandbox` 约束冲突。修复仅让 check 接受覆盖；公开 CLI 红测试证明旧版实际返回 selected/0，修复后拒绝/2，manifest/JAR 字节不变。完整 Python 回归 47 项、1 skip；另在真实固定沙盒成功 select index-invalidation → check → select all → check，恢复原选择，没有启动宿主/重新构建。
2. **暂缓：原子 JSON 写入重复（heuristic）。** contract 与 session 均有 UUID 临时文件/replace/finally unlink，session 另有 Windows PermissionError 有界重试。确认重复，但没有已观察功能失败；本轮不为消除 smell 额外重构写入模块，后续可靠性修复应注意两处策略。它不是文档化标准硬违规。

## Spec

独立 Spec 轴仍待回报；未用自审替代。

## 冻结后完整回归：未通过，保留阻塞

- 原始报告：`.scratch/rider-acceptance/run-4e7cd7999863483aa788dc071df03d37/result.json`。
- runId `328b4ae2-5ef5-4c4a-bd4b-bf8ddc521805`，相同产品 SHA `c9effbb115283137727c6131c59c1e306769f5bb8e123a9c5913c3d8ef6e6159`。
- Complex all 状态 **assertionFailed**。174 个 UI 步骤中 9 passed / 1 failed / 164 notRun；第 10 步 `Web SDK static library` 超时。
- 新 UI journal 显示该请求 `83beb597-e4dd-4b7c-be4d-080fccf994a5`：RESOLVE reply accepted（epoch 9）→ NAVIGATE_TYPE submitted → CONTEXT_INVALIDATED（current epoch 10）。这是实际取消阶段/原因，不等于已确认触发 epoch 变化的根因。后端 bounded journal 后续采样已覆盖该时点，不能靠最终快照倒推来源。
- full SDK reporter 同 run/backend，**183/183 passed**；不等于完整 UI 旅程通过。
- 原有 project/reference/TFM reload、external source policy、decompiler policy/failure distinctions 等范围外 pending 保持原样，没有抹成通过。
- 按失败即停边界，没有重跑整个 Complex，也未继续 Legacy。该失败仍是最终验收阻塞，不宣称 retro 整体最终验收完成；新独立诊断 case 的成功仍只声明自身。
- 主代理独立核验原 frontend/backend 已退出；cleanup/resource errors 为空，原设置 1469 字节及全部 69 项夹具 hash 匹配。未 kill、未接受 Trust/Legal、未 push。

完整离线/仓库检查仍待独立验证代理回报。
