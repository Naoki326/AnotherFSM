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

场景筛选/分阶段复用及产品失效原因诊断是后续顺序改动；不要假设当前已经提供 `--case` 或业务 epoch 事件流。
