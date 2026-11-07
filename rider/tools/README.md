# 受监管 Rider 验收

入口为 `acceptance.py`；业务插件不引用这些工具。只操作固定的自有 `rider/.sandbox` 和仓库受控夹具，不启动业务程序，不自动接受项目信任或反编译许可。

## 准备、检查、执行

```bash
python rider/tools/acceptance.py prepare --suite legacy
python rider/tools/acceptance.py check --suite legacy
python rider/tools/acceptance.py run --suite legacy
```

suite 为 `legacy`、`complex`、`boundary`、`source`。同一 suite 决定准备参数、入口类与实际解决方案；准备不启动 IDE。source 仍需显式准备既有受控源资产：`prepare-source-boundary.py prepare`。run 会管理自有 loopback 服务与精确设置备份；发现过期或未知服务会拒绝启动，不杀占用端口的其它进程。

`check` 只读，逐项核对：

- `acceptance-prepared.json` 中的 suite 与当前请求；
- 真实 JAR 的 suite startup class、descriptor 注册及独立 `AcceptanceLifecycle`；
- 产品 ZIP 关联与安装文件 hash、observer 全量 hash；
- 固定解决方案、夹具源码及已准备依赖资产 hash；
- source 的 preparationId、资产、loopback health 响应，以及启动前原始设置备份 bytes。

`check --sandbox PATH [--source-preparation FILE]` 仅用于只读离线检查；这些路径覆盖不能用于 prepare/run。普通 `sandbox.py run --acceptance` 同样执行准备身份检查，旧的 hash-only observer 需要重新 prepare。

## 监督和退出

run 产生唯一 runId、日志目录和结构化结果；stdout 只打印摘要，永久日志位于 `.scratch/rider-acceptance/run-*/`。运行时不会把一次请求 dispatch 或进程启动当测试通过。

- 独立生命周期 heartbeat 与实际 suite 主报告共同证明启动。
- 单个 partial 报告不会提前停止尚未完成的相关 reporter。
- 缺报告、JSON 临时不可读、pending 与真正 assertion failure 分开。
- 首先请求各测试旅程执行自己的受保护恢复，再通过公共 `Application.exit()` 正常退出。
- 只允许本 JVM 实见的普通 Confirm Exit / Exit；不处理 Save/Discard、Trust、Decompiler Legal。
- 确认记录的 frontend 及全部 run-correlated backend 的 PID/creation FILETIME 都已退出后，才还原设置的精确原始 bytes/原始缺席状态。PID 复用、身份缺失或退出受阻会保留备份并明确报告，不 forcekill。
- 最后核验夹具字节未漂移，自建 server 已停止。复用但不属于本次启动的匹配服务不会被停止。

参数 `--startup-timeout`、`--timeout`、`--cleanup-timeout` 都为有界秒数。source 可能等待真实许可对话框；观察并明确选择后再继续，不由工具代按同意。

退出码：0 为此 suite 所有必需报告通过且清理成功；1 为断言失败；2 为准备/会话错误；3 为未启动/心跳停滞；4 为未完成验证；5 为清理受阻。scope partial 原样保留，不冒充整票 AC 已通过。

若退出受阻，先查看该 run 的 `cleanup-receipt.json`、`resources/resource-cleanup.json`、`acceptance/exit-observation.json` 及实见沙盒窗口。保持备份，确认 frontend/backend 都已关闭后使用 `prepare-source-boundary.py restore-settings --confirm-sandbox-closed --backup-id ID` 恢复对应原备份；不通过写默认值恢复，不移除活动锁来绕过保护。

## 离线回归与当前实证

```bash
python -B -m unittest discover -s rider/tools/tests -v
```

已接入既有 Checks 和 master Deploy 工作流。契约测试不需要 Rider：公开 CLI 检查真实临时文件/JAR/loopback 服务；监督 seam 使用真实短生命周期 Python 父子进程，验证超时/异常/中断后清理、PID 复用、backend 缺失及 pending 不升级为成功。

2026-11 的受控 Windows/Rider 冒烟验证了 40 秒主动超时后正常旅程取消、frontend/backend 退出、原设置精确还原和夹具无漂移；退出码 4 保留了未完成测试，不声称产品全量通过。首次实测还纠正了 Rider 自定义插件 loader 的空 CodeSource：现在通过公共 plugin descriptor 的 loader/path 验证身份。

## 独立场景与阶段准备

```bash
python rider/tools/acceptance.py cases --suite complex
python rider/tools/acceptance.py prepare --suite complex --case generic-native
python rider/tools/acceptance.py run --suite complex --case generic-native
# 只切换已准备的冻结 bundle，不调用编译器/restore/夹具准备：
python rider/tools/acceptance.py select --suite complex --case configuration
python rider/tools/acceptance.py run --suite complex --case configuration
```

Complex 支持 `generic-native`、`configuration`、`source-matrix`、`index-invalidation`，另有保持原验收的 `all`。每个独立 case 自建所需输入并恢复，只声明自身结果；轻量 SDK `ready` 不冒充全 SDK checks passed。实际泛型场景只执行6步、约28秒，不先重放完整174步。

`prepare` 将 product、fixture、observer 分开，产物输入与输出 hash 均匹配才复用；阶段结果在 `acceptance-phases.json`。`--force`/`--no-cache` 强制重做。完整输入包括源码/传递引用/linked文件、props/targets、SDK/JBR/host、包和编译配置；实际子进程不继承动态 `PI_*`，不能一边忽略 fingerprint、一边让 MSBuild 消费它。

实际 Legacy 重复准备已验证 product / fixture / observer 三阶段全部 cached；输出丢失/篡改、SDK/配置/传递依赖变化的 miss 由离线契约回归验证。

**安全限制**：未知构建逻辑保守 miss。环境指定的 Directory.Build / Directory.Packages 导入、CustomBefore/After、MSBuild/编译器 override 等未证实的隐式输入也会保守 miss，不把相同路径字符串当成外部文件内容已核验；不改实际编译环境。Complex 的 global.json 选择和 ILLink/WebAssembly 自定义 package 输入闭包尚未证实，当前 fixture 阶段不会标 cached；不要将该限制写成已消除。需要换 case 时使用 `select`，它先核对已安装 bundle、夹具及依赖未漂移，再仅更新运行选择；它不声称重新构建过，也不替代源码/SDK变化后的 prepare。产品和 observer 阶段能分别复用，不因更换 case 而重编译。

## 结构化失效与取消诊断

产品提供只读 `ScriptIndex.DiagnosticRecords` 与 `SemanticUi.diagnosticRecords()`：分别最多保留 512 / 256 条不可变 plain-data 记录。读取不会提交 PSI、刷新索引或推进业务 epoch；不增加运行时依赖。

- 后端记录事件来源、实际进入的 Submitted / Commit / Resolve / Navigate / Reply 阶段、requestId、前后业务 epoch / type / structure version、PSI cache timestamp、输入摘要及是否变化。相同文本的 cache-only 观察与实际文本变化分开；摘要只有 hash，不保留路径、文档或请求全文。
- 取消只记录实际观察到的阶段，原因无法直接证明时为 `Unknown`，保留原异常传播，不从 stamp 变化推断取消原因。
- 前端记录真实请求阶段（含 host/source chooser）、文档版本及结果/当前 epoch，区分 document changed、transport changed、superseded、timeout、reply rejected、用户取消等原因。不保留编辑器、回复 JSON 或文本。
- observer 导出 `index-diagnostics.json` / `ui-diagnostics.json`，附当前 runId 与进程身份；只读取已存在的产品服务，不为观察创建服务。它们是诊断，不单独充当验收成功报告。

```bash
python rider/tools/acceptance.py prepare --suite complex --case index-invalidation
python rider/tools/acceptance.py run --suite complex --case index-invalidation
dotnet test rider/tests/diagnostics/IndexDiagnostics.Tests.csproj --nologo
```

独立 `index-invalidation` 通过公共 SDK cache 操作、真实 C# buffer 编辑、正式动作与实际 loopback transport 断连检查业务失效与取消诊断；不伪造事件或读取产品私有字段。真实 Rider 已通过 10 步及 6 项 cache-only 检查，观察到 HOST_CHOICE 阶段的 DOCUMENT_CHANGED / TRANSPORT_CHANGED，同 backend 重连且清理后设置和夹具精确恢复。证据及范围见 [`../evidence/retro-123/README.md`](../evidence/retro-123/README.md)。纯 journal / CLI 测试及编译不等于真实 Rider 场景通过；独立 case 不外推整个 Complex 矩阵。
