# Rider retro 1/2/3

## 范围与验收标准

本次是 #34 完成后的基础设施复盘，不是 #35；不重开已关闭的票或地图。固定基线 `45e7ad1`。

1. 统一受监管 runner：准备身份可核验；有界启动/运行/清理；测试先恢复自身改动，再正常退出；核验关联 frontend/backend 真正退出后精确恢复原设置；未完成、失败、清理受阻分开。
2. 场景可独立运行，准备阶段可复用：独立 case 不先重放全量旅程；product / fixture / observer 只有输入及输出均核验才复用，未知闭包保守 miss。冻结 bundle 可用 select 切 case，但不能冒充重新构建。
3. 结构化失效/请求取消诊断：只读有界 plain-data、来源/输入摘要变化/前后业务版本及 SDK stamp/实际请求阶段；无全文、路径、配置秘密和额外生产运行时依赖。真实 SDK 无文本 cache 变化不使已有 occurrence 失效；真实 C# buffer 编辑必须失效；真实前端编辑及 transport 取消有阶段/原因实证。无法证明的后端取消原因保留 Unknown。

同意的测试 seam：公开 runner CLI 离线契约、公开只读 SDK 诊断、真实 Rider 正式操作。不读产品私有字段，不伪造事件，不以绑定成功代替 readiness。

## 已核验实证

机器可读摘要与原始报告 hash：[`focused-validation.json`](focused-validation.json)。完整原始日志留在本机 `.scratch/rider-acceptance/`。

- runner 主动 40 秒取消：`run-4c434f8a45904c60b1233a2efa245fe5/result.json`，正常恢复/退出；status 保留 verificationPending，不冒充通过。
- 独立 generic-native：`run-7d3333adc7f7461090475bb858fc07dc/result.json`，6 步约 28 秒，完整矩阵未运行。
- Legacy 重复准备：`prepare-legacy-warm.log`，product / fixture / observer 全 cached。Complex fixture 的 ILLink/WebAssembly 自定义 package 外部输入闭包未全证实，持续保守 miss。
- 新 case CLI：先出现 unknownCase 的真实红，再通过新增 case/冻结产物 select 检查。
- Python 离线 47 项，1 项因 Windows symlink 权限跳过；SDK-free 后端 journal 13 项通过。
- 产品及完整 observer 对真实 SDK 编译通过；Java observer 有既有 deprecated API 警告。
- 真实独立 index-invalidation：runId `25824cbe-6037-4c4e-b24a-cd4851d756a1`，`run-d9fe0548146a48d79a5489e5788d7bdd/`。10 步、6 项 cache-only 检查通过；无文本变化保持相同 Finder occurrences 有效及业务状态不变；真实 C# 编辑推进业务 epoch/输入摘要并使结果过期；HOST_CHOICE 阶段观察到 DOCUMENT_CHANGED / TRANSPORT_CHANGED；同 backend 自动重连。原 frontend/backend 已退出、设置原始 1469 字节及 69 项夹具 hash 精确匹配。

SDK `ready` 始终只代表 readiness，不是 full SDK checks passed。独立 case 结果只声明自身；冻结后的全量回归及双轴审查另行记录，不能复用旧 #34 证据冒充本次通过。
