# #32 Rider 日常导航闭环

状态：实现及主要真实宿主验收已完成；最终交付审查与传输重连证据仍在收尾，不能据本记录提前关闭 Issue。

规格：[Issue #32](https://github.com/Naoki326/AnotherFSM/issues/32)。只验收本票 AC；#33 全名称/Module/import、#34 复杂项目/外部依赖/覆盖、#35 完整编写辅助、#36 性能与最终安装包矩阵不在本票。

## 实现入口

- `AnotherFSM.Semantics/`：从权威 grammar 生成独立 .NET 解析器，保留普通声明/引用、UTF-16 位置和可信头部；无 Rider PSI 或业务实例。
- `shared/DeclarationRules.cs`：运行时、既有声明生成器和新核心共用名称/隐式工厂键/旧组合拒绝规则。
- `rider/backend/`：只读宿主 C# PSI、真实特性及契约身份、当前项目工厂关联、原生 Find Usages、版本缓存与协议查询。
- `rider/frontend/`：轻量文件接入、宿主共享文档绑定、异步正式动作和原生 Find 标签页过期提示；不另写绑定/作用域语义。
- `rider/protocol/`：生成的 Solution 扩展；query、invalidated 都异步；不另开 socket/workspace/LSP。
- `rider/tests/host/`：独立受控宿主测试插件，不进入开发 ZIP。构建与安装入口见 [Rider README](../../rider/README.md)。

## 已核实的目标宿主

Windows 11 x64，OS build 26200；Ryzen 7 3700X；Rider 2026.2.3.1 / RD-262.10968.170；JBR 25.0.4；.NET SDK 10.0.300。生成期官方模型资产来自 .171，实际双端对 .170 编译并完成真实 wire 往返；该差异没有隐藏，也不承诺其他版本兼容。

## 分层证据

| 层 | 已核实结果 |
| --- | --- |
| 核心 | 23 项测试通过：名称/键分离、UTF-16、局部恢复、冲突/契约/泛型安全降级、并发替代、缓存边界与关闭 |
| SDK/构建 | 精确安装 SDK 编译；生成代码可重复比对；开发 ZIP 曾两次逐字节相同，最终字节待收尾再核验 |
| 真实宿主 | 两个独立项目 Orders/Inventory；.fsm/.FSMScript；真实 RD→共享语义→DSL/C# 正式动作；C# 声明与使用处原生 Find Usages |
| 真实未保存文本 | 关闭脚本后仍贡献原生引用；未保存改键时磁盘保持原文，旧页出现“结果已过期”；Rerun 后只保留当前隐式键；C# 未保存 Key 更新也使旧结果失效 |
| 恢复/绑定负例 | 完整头半截 body 仍绑定；半截头无确定关联；同键冲突、假同名特性、非 IFSMNode、无效 named Key、开放及外层泛型均不确定绑定 |
| 无宿主 | 非项目脚本仍能定位局部 Node；C# 操作明确上下文不足 |
| 旧位置 | 真正的过期 occurrence 通过公开 SDK Navigate 入口返回 false；不把此 SDK 调用冒充 GUI 双击 |
| 关闭/断开 | 核心关闭与并发替代测试通过；受控 backend 退出已观察到 wire=false、进程死亡及旧请求取消，没有旧跳转；恢复后往返仍待独立证据 |
| 回归 | 完整 .NET solution 458 项、React 12 项、仓库工具 22 项、发布工具 6 项通过；Typecheck/lint 与生成器真实消费者通过 |

本地原始报告与帧：`.scratch/issue-32/sandbox-host-test-20261106T174716.302111Z-27b6ac37/acceptance/`。真实宿主测试不是只比较日志；它经正式动作选中真实编辑器位置，并从原生 descriptor 读取实际引用/范围，保存可见截图。

## 关键修复与事实边界

1. 宿主会发空 PSI 节点通知；修复后同一真实环境记录了 303 次此类通知，不再抛空引用。
2. 自定义 LanguageFileType 不自动获得持续 backend 文档绑定；通过公开 `RiderCustomBackendLanguageSupport` 接入宿主 synchronizer，而非另存一套 Java 语义。
3. 同一文档可有多个 PSI 视图；仅在项目、TFM 与文档对象均一致时去重，排除 Misc/Solution 伪宿主，真实多上下文不取首项。
4. 原生无效行可能被 Rider 隐藏，仅后端 IsValid=false 不足以作为可见过期提示。正式实现标记实际 Find 标签页，并监听该页真实 ReRun/isSearchComplete，不重排旧列表。
5. 原生查询按整次查询捕获代次，不逐文件换代；文本/类型变更使老查询取消，不能混合多个代次。
6. 常规源码读取只用 PSI 静态元数据。插件不引用运行时程序集，不执行注册、工厂、getter 或特性构造器，不启动业务 MSBuild/restore/生成器、下载或认证。测试夹具的显式构建、运行时/生成器回归执行，与安装插件的分析流程严格分开。Rider 自身受信任设计时加载不是插件新增执行。
7. 故障注入只针对测试 JVM 的已验证 direct-child backend；目标 Rider 的进程致命故障展示全 IDE Restart，不把测试自行要求的“同 JVM 换 PID”当成产品 AC，也不声称这种故障已自动恢复。

## 未承诺

未测量最终 P95/内存预算，不扩大 OS/TFM/Rider 版本范围，不宣称全量 DSL、外部源码或 Quick Documentation 已交付。详细最终 verdict 与双轴审查将在 #32 交付评论及本记录收尾。
