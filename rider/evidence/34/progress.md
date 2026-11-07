# #34 实施证据（未完成验收）

基线 `bfa3fc7f5d4580549e7a4b62bfed47c84bd87f4b`，本票仍 OPEN。以下仅为中途回归，不是最终 ZIP 或完整 #34 验收。

- 核心公开查询：71 项通过；新增覆盖/反查、身份、配置相对路径及预注册 Module 有红绿日志，原始目录 `.scratch/issue-34/core/`。
- 后端 SDK 多次构建 0 warning / 0 error；中途开发 ZIP SHA-256 `5ee879577f52d8b83fe21d503afba819b4778821348ff1fe66cabeb26bac0781`。
- 该 ZIP 在 Windows 11 x64 / Rider 2026.2.3.1 / RD-262.10968.170 的隔离沙盒中通过既有 #32 的 51 阶段、#33 的 117 项、SDK 发现/单源域检查以及真实 socket 断开恢复。原始目录 `.scratch/issue-34/host/regression-14a534d58bd545d5b4b91acd1e68e14b/acceptance/`。
- `preintegration-host-regression.json` / `preintegration-dsl-regression.json` 是对应的真实报告；保留其自身原工单测试标签，避免追溯改名成 #34 矩阵验收。
- 夹具首轮 Debug/Release 离线准备通过，正解决方案构建 0 warning / 0 error，缺 DLL/包负例如预期失败。DLL 准备出现本机已有框架包 NU3005 签名警告，未屏蔽/修包。初始 StaticBrowser 只是普通 SDK browser library，不算 WebAssembly SDK 验收。已另补 `StaticWasm`：实际 WebAssembly SDK / net10.0 / browser-wasm；Debug/Release 生成 WASM、WebCIL 及程序集且哨兵入口未运行。原始准备摘要分别为 `.scratch/issue-34/fixtures/20261107T010510Z-732c557b/summary.json`、`20261107T010541Z-29bd7d23/summary.json`。
- 外部呈现已实现并接入正式导航：精确宿主 provider 白名单、只读 consent guard、不走下载/retarget，任务生成/定位后重验 lifetime/epoch。编译通过，不等于真实呈现通过。复杂上下文新旅程、最终全套回归、双轴审查和远端交付尚未完成。不得根据这些中途回归关闭工单。

正常确认退出沙盒，并按记录的 Windows PID/创建时间回读已停止；未终止日常 Rider。
