# #2：新模块 DSL 最终复验与交付

- 工单：[GitHub #2](https://github.com/Naoki326/AnotherFSM/issues/2)；规格原文：[spec.md](../spec-2-acceptance/spec.md)。
- 本窗口仅处理 #2；开始时 #3/#4/#5/#6 均已关闭，父票未关闭阻塞数为 0，工作区干净。
- 本次基线：`93160043fded1c4b0136f7c29f40a0ce35a140d0`，分支 `develop_new_module`。
- 整体审查基线：`06322e367f3f032315d8975b7e1ce08dbd6bebc5`。
- 本报告取代[首轮未通过结论](../spec-2-acceptance/ACCEPTANCE.md)的当前状态，不抹去历史复现及 #6 的独立交付记录。

## 结论

功能与验证通过。#6 的 Root／模块内部成员、连接及结束事件缺口已闭合；本次又修正运行高亮与脚本声明分析中残留的前缀推断。保留编译期展平、既有公开消费入口及普通事件模型，没有新建运行时模块或重做 UI 架构。

## 本窗口补齐的实际漏项

| 问题 | 修复 | 可自动失败的验证 |
| --- | --- | --- |
| WASM 普通 `A.Observer` 执行时模块 A 也被高亮 | 在既有 `Nodes` 视图投影提供实际 UI 使用的 `IsActive`；模块根据 `ExpandedNodeNames`，普通／内部节点根据真实名字；Razor 不再按前缀判断 | `DemoRuntimeFacts.ActiveNodeProjection_HighlightsOnlyTheRealNodeOrItsOwningModule` 两个案例，覆盖普通节点、真实成员、模块内视图及 Stop 清除；真实浏览器先红后绿 |
| React 活动节点、活动转移及数据同步仍按前缀折叠 | 所有相应消费点复用已有 `ownerOfNode`，与静态画布成员身份一致 | [真实 UI 回归脚本](react-active.browser.js)两条执行流程 PASS；普通节点及 Launch 边正确高亮，真实 Retry 在 Root 和模块内正确显示 |
| `ReadScript` 将 Check 的公开 `Sub.Done` 错归给独立实例 Check.Sub | 仅投影同文件模块及 import alias 的公开声明元数据，复用核心 `FsmModuleInterfaceResolver.FindPublicOwner`，不再选择最长前缀 | `ScriptAnalysisFacts.ReadScript_ResolvesDottedOutputsByDeclaredPublicIdentityNotLongestPrefix`：先经真实 Engine 验证脚本合法和真实连接，再断言 ReadScript 回调源；旧实现错报 Check.Sub，修复后通过 |
| 不相关局部 N.Area 遮蔽真实限定出口 Area.Check.Done | 出口先按局部／完整限定公开身份解析，实际公开接口优先于词形前缀 | `ReadScript_FullyQualifiedOutputIsNotShadowedByAnUnrelatedLocalPrefix`：Engine 成功而旧 ReadScript 明确失败，修复后通过 |

`ReadScript` 仍是声明投影，不加载外部文件或展开模板。缺少外部模板且多个实例都可能拥有该出口时，明确报歧义并指向 FSMEngine 构建解析；唯一未知候选保持既有引用投影合同。`ReadScript_RejectsAmbiguousExternalOutputOwnershipInsteadOfGuessing` 与既有注册／别名投影案例保护此边界。没有为测试增加专用公开 seam。

## 最终验证（实际重新执行）

环境：Windows / Git Bash，.NET SDK 10.0.300、Node.js v25.9.0。输出日志保存在本目录，按仓库规则忽略；本报告、截图与 UI 回归脚本版本化。

| 命令／入口 | 结果 |
| --- | --- |
| `dotnet build AnotherFSM.sln --nologo --artifacts-path .scratch/spec-2-reacceptance/artifacts` | 完整构建通过，0 错误；最终日志 76 个警告，含 API、WASM、分析器及生成消费者 |
| `dotnet test AnotherFSM.sln --nologo --no-build --no-restore --artifacts-path .scratch/spec-2-reacceptance/artifacts` | 核心 **92/92**、API **35/35**、WASM **8/8**；共 **135/135**，0 失败、0 跳过 |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo --artifacts-path .scratch/spec-2-reacceptance/artifacts --filter FullyQualifiedName~ScriptAnalysisFacts` | 单文件 **10/10**；两次实际行为红灯分别见 `analysis-red.log`、`qualified-output-red.log` |
| `dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --nologo --artifacts-path .scratch/spec-2-reacceptance/artifacts` | 单文件 **8/8**；新增视图属性前编译红灯，原真实 UI 错误另有 DOM 红灯断言与截图 |
| `dotnet .scratch/spec-2-reacceptance/artifacts/bin/FSMScriptAnalyzerTest/debug/FSMScriptAnalyzerTest.dll` | PASS：生成代码真实初始化、查询及执行断言 |
| `dotnet run --project .scratch/t2-toolchain/packed-consumer/Consumer.csproj --no-restore --no-launch-profile` | PASS：此前构建的本地 analyzer 包消费者重跑；本次不冒称重新打包发布 |
| `dotnet run --project .scratch/spec-2-acceptance/wasm-probe/Probe.csproj --artifacts-path .scratch/spec-2-reacceptance/artifacts --no-launch-profile` | 原失败探针现 exit 0，Root 保留 A.Observer，普通 Observe 源保留 A.Observer，A 内仅 Entry/Retry |
| 前端 `npm run typecheck`、`lint`、`test`、`build`（均带 `--prefix demos/FSMDemo.Frontend`） | 全通过，测试 **6/6**，0 跳过 |
| [react-active.browser.js](react-active.browser.js) | 通过 browser 工具执行已落盘原文，结果 `A.Observer: Root PASS`、`A.Retry: Root PASS`；真实 UI/SignalR，无 store 注入 |
| `git diff --check` | 通过 |

保留既有 Microsoft.OpenApi NU1903 高严重性安全警告、可空／未使用成员／打包警告，以及前端 SignalR PURE 注释／大包提示；本票不扩大为依赖升级。交互式 FSMNodeAnalyzerTest 仅构建，未执行，不混称为自动测试。使用独立 artifacts，未强停其他会话进程或覆盖锁定产物。

## 父规格 Testing Decisions 1–14 最终映射

| 项 | 合同 | 通过的公开入口证据 |
| --- | --- | --- |
| 1 | 显式实例、元数据、无运行时代理 | ModuleDslFacts、生成消费者 |
| 2 | 普通进入模块、出口到普通／模块 | ModuleScriptFacts、ModuleDslFacts、EngineEditingFacts |
| 3 | 单／多／零入口、公开选择和非法入口 | ModuleDslFacts、EngineEditingFacts |
| 4 | 非末端／多个发布者、Branch 去重 | ModuleScriptFacts、MultiplePublishers 用例 |
| 5 | 实例隔离、同名事件与出口、声明顺序 | ModuleScriptFacts、OutputOrder、ModuleExportFacts |
| 6 | 私有／未知接口、普通节点冒充、无发布者拒绝 | ModuleScriptFacts、InvalidOutputConnections |
| 7 | 转移冲突和真实名称碰撞拒绝 | ModuleDslFacts、API 编辑原子性用例 |
| 8 | Namespace、点号身份、不相关前缀不改变归属 | 核心与分析测试；WASM 真实成员合同；本次 ReadScript 两项新回归及 React/WASM 真实执行 UI |
| 9 | 普通事件、数字／命名 Branch、事件优先不变 | ScriptAnalysisFacts、PlainScopes/ATest 生成消费者 |
| 10 | 淘汰旧 def、terminal、映射及占位源；失败可观察 | ModuleScriptFacts、ModuleLoadingFacts、InvalidSyntax/Try 边界 |
| 11 | 同文件／注册／相对文件导入、缺失／循环错误 | ModuleLoadingFacts、递归 AdditionalFiles 生成消费者 |
| 12 | 唯一导出、去重、元数据、多入口往返 | ModuleExportFacts、本次 React Demo→创建→接线→Export→原文 Re-import |
| 13 | 转移重建、分析、代码生成初始化 | Transform 生命周期回归、ScriptAnalysisFacts、生成消费者 |
| 14 | 现有演示适配且普通节点行为不变 | EngineEditingFacts、WASM 8 项服务合同、本次三段浏览器旅程 |

文档、示例及 ADR-0001 在已交付子票中完成，ADR-0002 未变；此次不重做它们。首轮报告中的完整测试名称索引仍适用，本次重跑对应全套测试，而非仅复述旧测试结果。

## 浏览器证据

三个顺序旅程均 ready 且 audit，无遗漏项；全过程通过真实页面操作，不替换数据层：

- WASM 默认模块：[01](01-wasm-default.png)；普通同前缀节点 Root：[02](02-wasm-root.png)；模块内真实成员：[03](03-wasm-inside.png)。
- WASM 活动归属：[红灯](04-active-red.png) → [绿灯](05-active-green.png)。旧宿主构建后资源清单过期曾导致重载超时，改启新宿主 :5203 后对原脚本重新验证通过，不把超时当成功。
- React [Demo](06-react-import.png) → [创建 FinalCheck](07-react-create.png) → [FinalCheck.OK 到 End](08-react-wire.png) → [新 DSL 导出](09-react-export.png) → [原文回导](10-react-reimport.png)。Export 文本与回导 textbox 逐字一致后提交，恢复相同接口边。复用已有 :5079 API / :5174 React；API 功能自 #5 未变，另有本次隔离构建的完整 API 合同测试。
- React 运行身份：[红灯](11-react-active-red.png) → [普通节点绿灯及真实 Launch 边](12-react-active-green.png)；[真实 Retry 模块内](13-react-member-inside.png)、[Root 模块 A](14-react-member-root.png)。

布局／标签重叠不是本票重做范围。执行回归使用明确 Pos、Fit View；活动动画使自动稳定性检查无法点击时，通过已核对的模块卡片点击进入属性，再使用 Enter，不靠注入内部状态。

## 审查与交付

独立 `code-reviewer` 的 Standards / Spec 两轴审查完整合并 diff，并复核本窗口补丁；结论见 [review.md](review.md)。审查不代替上方实际测试。正常 commit、push、工单证据评论及远端 CLOSED／commit 回读作为最终交付步骤；以 GitHub 的实际远端状态为准。
