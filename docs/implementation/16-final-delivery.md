# #16 最终交付：主代理独立验收

## 范围与源码身份

- 父规格：[#16](https://github.com/Naoki326/AnotherFSM/issues/16)。用户本次明确要求按 implement-spec 完成整个规格，覆盖历史的单票会话组织限制，功能 AC 不变。
- 单一集成分支：`implement/context-lifecycle`；本次实施固定基线 `07c2f08334dcc641117d2505a1ba3fea5d42f678`，未改写 `master`。
- 最后受测实现：`9e228babeba30190ec9881c089ad0314c40a4276`；记录提交 `6347f5471ea7555d0ea8e3e8dc01b06d02331d0b`；合入集成 `25be4e39e552369e0a23298a9e9463f5f928a48d`。
- 独立验收树与集成树完全一致：`45db7458da37de79c7bc85486b1086b1ed60b0b9`。本文件及后续验收收尾仅为文档元数据，不改变受测生产或测试源码。
- [完整追溯矩阵](22-final-acceptance.md) 对应父规格全部 **12 个验收族、58 个用户故事**及 #22 全部 AC；各票证据：[19](19-execution-lifecycle.md)、[20](20-event-sharing.md)、[21](21-consumer-host-migration.md)。#17/#18 的正式历史提交和审查记录由矩阵直接链接。

## 主代理最后一次独立门禁

以下均由主代理在与最终集成同树的独立 worktree 实际运行，不仅引用实施者报告。JRE/SDK、命令及分层边界沿用各票记录，npm 在 Windows 显式指定 Git Bash runner。

| 层级 | 实际命令或合同 | 最终结果 |
| --- | --- | --- |
| Debug 全方案 | `dotnet test AnotherFSM.sln --nologo -p:NuGetAudit=false` | 核心 **279**、API **83**、宿主 .NET **42**；0 失败、0 跳过 |
| Release 全方案 | `dotnet build AnotherFSM.sln -c Release --no-incremental --nologo -p:NuGetAudit=false` | **0 错误、66 个警告** |
| Release 测试 | `dotnet test AnotherFSM.sln -c Release --no-build --no-restore --nologo` | 核心 **279**、API **83**、宿主 .NET **42**；0 失败、0 跳过 |
| 类型化编译 | `bash tools/test-typed-context.sh` | 正例/执行及 **8** 个真实编译负例通过，含生成消费者错误聚合 |
| 最终删除编译 | `bash tools/test-final-contract.sh` | 正例及 **12** 个真实负例通过：旧构造、四个 Restart、旧 Context/两类包装、Handle 均不可用 |
| 旧组合编译 | `bash tools/test-removed-composition.sh` | 正例及 **9** 个真实负例通过，含六种旧组合类型和专属入口 |
| 真实生成消费者 | `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj -p:NuGetAudit=false` | 类型化访问器编译、查询、真实 Module、固定引用/共享/覆盖/下一轮输入组合执行成功 |
| 前端 | npm `test`、`run typecheck`、`run lint` | **12/12**，类型与 lint 均退出 0 |
| 真实 Release WASM | `RUNTIME_CONTRACT=true bash tools/build-browser.sh` | 重新 publish .NET Release WASM 及 React 生产构建成功 |
| 真实浏览器同合同 | `PORT=5180 node tools/run-browser-tests.mjs` | Chrome 后端离线 **WASM12/12**，随后真实 **API7/7**；两 JSON 均 0 skipped、0 unexpected、0 flaky |
| 正常发布包 | `env -u RUNTIME_CONTRACT bash tools/build-browser.sh` | 成功；存在 `index.html`，不包含 `test/runtime.html` |
| 清理与格式 | 浏览器后实连 5079/5180；`git diff --check` | 两端口均 closed；无格式错误 |

共 **29** 个真实编译负例，不将静态关键词查找算作编译证据。宿主 .NET 的初始化/非协作/真退出同步点测试不冒称浏览器探针；真实 Chrome 合同独立覆盖业务绑定、缺失来源、重复启动拒绝、停止后新输入、显式导入/旧通知隔离、Module 投影/编辑导出、安全加载和网络隔离。

原始主代理日志、两份浏览器 JSON 和编译诊断：`C:/work/anotherfsm-context-19/evidence-main-final-fixed/`。实施者的 red/green、TRX 和完整门禁：`C:/work/anotherfsm-context-19/evidence-final-fixes/`；此前最终候选及各票首次失败证据另存，未用后续绿色抹除。版本化的唯一技术结论入口是本目录，而非 ignored `.scratch` 或临时目录。

## 最终整分支双轴审查

两个正式审查均通过界面可见的 `Agent` 工具，使用 `code-reviewer` 类型，彼此独立，明确禁止再次委派/模型 CLI/隐藏审查。主代理对每个发现亲读规范、源码和公开复现，再决定处置。初审比较基线 `07c2f08` 至完整候选 `4f78933`，随后复核唯一补丁至 `25be4e39`。

### Standards

初审：硬规范违规 **0**、判断性 smell **0**；发现 **1 个 P1**：全方案非起始节点初始化时，停止只取消起点。主代理亲自复现，重开原责任票 #19，不把前票缺口藏入 #22；由一个可见实施者统一最小补修。修复取消实际初始化节点、停止后关闭后续初始化准入，用户初始化/取消仍在锁外，原真正退出与 inline 取消屏障保留。

原审查员复核：**原 P1 闭合，新增实际缺陷 0、硬违规 0、smell 0、推测 0**。独立 Debug 核心生命周期/共享选集 **113/113**、API 退休族 **26/26**、Session 退休族 **12/12**。

### Spec

整父规格初审：缺失/部分、范围蔓延、实现错误、推测均 **0**。此轴的 0 没有抵消另一轴已复现的 P1。

补丁独立复核：符合父规格停止/真正退出要求，**新增实际缺陷 0、要求缺口 0、范围蔓延 0、推测 0**。独立 Debug 焦点选集核心 **73/73**、API **26/26**、宿主 **12/12**。

**最终剩余发现：Standards 0；Spec 0。** 主代理另写的公开探针确认：实际 Next 收到取消，初始化尚未返回时 Stop/Completion pending 且引擎仍占用；放行后完成成功、占用释放、后续初始化与业务运行次数均为 0。

## 交付与已知边界

- 目标 API、全部消费者、普通 Module、三实际工厂、生成消费者、Demo、API/WASM 和双语指南均已闭合；无业务依赖流程真实使用 `engine.Prepare()`，不制造空业务对象。旧 Context 包装/整包传播、公开执行构造/Restart/Handle 已删除，不留受支持兼容层或动态回退。
- 普通启动不抢占，显式替换等待真正退出；集合共享只传业务事件，不共享业务对象或控制。调用方继续拥有业务资源；非协作初始化/节点/取消回调不返回时继续占用，不强行终止或伪造完成。
- 没有已知未满足的父规格或 #22 AC。技术验收通过后由主代理正常 push、回读远端、按 GitHub Issues 方式依次关闭补修后的 #19、#22、#16，再清理本次实施/review worktree；不创建 PR、不合入或改写 master，不清理独立历史原型。
- 既有 **66 个构建警告**、反射/Reactive/Autofac 裁剪及 React 构建警告保留。npm 安装仍报告 **18 个漏洞（11 moderate、7 high）**；未越票升级依赖或改 lock。NuGetAudit 关闭不表示 .NET 依赖没有安全风险。
