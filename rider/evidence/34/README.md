# #34 验收索引

实现、本地检查与独立审查已完成。远端交付以 [#34](https://github.com/Naoki326/AnotherFSM/issues/34) 的最终评论、关闭状态和远端 commit 回读为准；不修改已关闭父票/地图，不开始 #35/#36。

目标：Windows 11 x64；Rider **2026.2.3.1 / RD-262.10968.170**。最终实现 `c1ffd56`，固定审查基线 `bfa3fc7f5d4580549e7a4b62bfed47c84bd87f4b`。

## 最终同一包

`final-package.json`：`rider/build/tools-build/anotherfsm.zip`（由仓库脚本本地生成，不把二进制提交到 git）。

SHA256：`01e1f74524c7de30e4f80faecd972c87989b122dd286fa5c3ce94382ecee7c6b`。同一实现提交重复构建字节一致；安装后五个运行时文件的 hash 与 ZIP manifest 全部匹配。产品包不包含测试 observer。

| 层次 / 范围 | 结果 | 永久证据 |
| --- | --- | --- |
| SDK-style、来源、身份、隔离 | 七个项目/TFM × 四种来源 = **28 组合**；实际 SDK Finder 域、完整身份及正常 C# 结果保留全部通过 | `sdk-28-contexts-green.json` |
| 正式 Rider 操作 | **174/174**；逐组合 host/TFM 选择、导航或可见降级、原生 FindUsages、物理去重和 host 分组、泛型/别名/坏值、配置覆盖/相对 Module/键比较/旧结果失效 | `complex-ui-28-contexts-green.json`、`final-native-package-references.png` |
| 生命周期 | **62 项通过**；真实 Compile/ProjectReference/TFM 重载、原生 Unload/Load、旧 DSL/native 行拒跳、同已拒信任目录的局部 DSL/no-borrow | `lifecycle-boundary-green.json`、`declined-local-dsl.png` |
| 独立来源/策略 | **12/12**；实际策略禁用、未接受 consent、允许反编译、受控 offline/401、版本不匹配、符号不足、真实方法级反编译失败，绑定/元数据保留、来源不提升 | `source-policy-green.json`、`source-consent-witness.json`、`actual-legal-dialog.png` |
| 真实拒绝信任 | 实见 Don't Open，宿主 Opening was canceled；MSBuild InitialTargets 哨兵前后缺席 | `trust-refusal.json`、两张 `real-trust-*.png` / `real-trust-opening-cancelled.png` |
| 旧产品回归 | **51 阶段 + 117 项**；SDK 单源搜索域；真实同进程 socket 断开/重连 | `final-legacy-host.json`、`final-legacy-dsl.json`、`final-search-scope.json`、`final-socket-reconnect.json` |
| 全量检查 | **.NET 507**（含核心 72）、React lint/typecheck + **12**、Python **6**、Node **22** | `final-checks.json` |
| 两轴审查 | Standards：0 硬违规，1 项低优先测试重复显式接受；Spec：首轮 P2 已修，最终有据缺陷/功能缺项 0 | `review.md` |
| 清理 | 自有沙盒和 loopback server 停止、夹具项目/源码原样、精确原始沙盒设置 bytes 还原、临时 DEBUG 清除 | `final-checks.json`、`source-settings-restoration.json` |

## 不提高证明等级

- 组合旅程与生命周期旅程顶层 `partial` 保留各自有意分离的 scope 说明。生命周期 58–61 的 policy pending **未涂绿**，由独立来源策略 12 项覆盖。没有将“未运行”当通过。
- 当前源码仅指宿主项目上下文。外部源码匹配证据不足时始终 **unverified**；SourceCSPath/PDB/Source Link 存在、导航 dispatch、打开同名文件均不证明程序集/文本匹配。
- 外部呈现只调用精确宿主 metadata/decompiler provider，沿用策略、不自行下载/认证/改变设置。允许反编译时，真实编辑器及 SDK header/MVID 才证明呈现，不能只凭返回成功。
- 受控 loopback 连接拒绝/HTTP401只证明相应源服务状态，不声称整机离线或访问真实私库/凭据。实际坏 IL 的宿主注释证明 **方法级**反编译失败，非整个引擎异常/取消。
- 明确插桩的注册/工厂/getter/特性构造器/入口哨兵没有触发；不扩大为整机进程/网络无副作用证明。受控夹具显式构建/打包与插件被动分析分开。
- WebAssembly 为实际 SDK 的静态 Debug/Release 构建，未执行业务入口、运行/发布/AOT 或部署。宿主 WASM 单元合同不冒充浏览器运行证据。
- 正常 Rider 后续运行会保存自身偏好；所有旅程关闭后再从不可变备份精确还原初始沙盒层，receipt 明确记录这次最终还原，不用写默认值冒充。

## 可重复操作

```bash
python rider/tools/prepare-complex-fixtures.py --configuration Release --probe-net8-wasm
python rider/tools/build.py
python rider/tools/prepare-acceptance.py --complex
python rider/tools/sandbox.py run --acceptance --complex
```

生命周期：关闭上述自有沙盒后，`prepare-acceptance.py --complex --boundaries`，再 `sandbox.py run --acceptance --boundary`。来源策略需显式准备、备份/还原自有设置及实见 legal UI，见 `rider/tests/fixtures/boundary/SourceCases/README.md`，不自动接受许可。

**不要构建/restore/授予信任给 `rider/tests/fixtures/trust-declined/Declined.sln`**。普通回归入口为 `prepare-acceptance.py` / `sandbox.py run --acceptance`。正式沙盒操作使用 PID/创建时间校验，不终止日常 Rider。

保留的真实红例包括 `sdk-red-alias-and-tfm.json`、`sdk-partial-catalog-red.json`、`override-source-ui-red.json`；不回写成事后全绿。原始日志与帧在项目 `.scratch/issue-34/` 和 `.scratch/issue-32/sandbox-run-*/acceptance/`，永久 JSON 保留其原 seam 和 scope。
