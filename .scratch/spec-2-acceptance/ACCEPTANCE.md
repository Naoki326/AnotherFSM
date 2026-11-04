# 父规格 #2 整体验收

> 本文为首轮历史记录。#6 修复后的最终复验与补齐结果见 [最终验收报告](../spec-2-reacceptance/ACCEPTANCE.md)，不要将下方历史 OPEN／失败状态当作当前状态。

- 规格：[GitHub #2](https://github.com/Naoki326/AnotherFSM/issues/2)，原文快照见 [spec.md](spec.md)。
- 验收代码：`4179c6a037f4645e689fbce1e8cb8cc9de18fee1`，分支 `develop_new_module`。
- 合并审查基线：`06322e367f3f032315d8975b7e1ce08dbd6bebc5`。
- 子票：#3（`d9f3ba9`）、#4（`a5b530a`）、#5（`4179c6a`），远端均为 CLOSED。
- 本次只做整体验收，不改产品实现，不重新实现已交付子票。

## 首轮结论（历史）

**未通过：WASM 演示仍按名称前缀猜测模块归属，导致合法普通点号节点与连线被误投影。父规格保持开启，原生阻塞关系已指向待修复票 [#6](https://github.com/Naoki326/AnotherFSM/issues/6)。**

核心、分析／生成、React 图编辑及模块感知导出检查均通过；以下结果不能被解读为整个解决方案零警告或所有演示均满足父规格。

## 全量检查（本次重新执行）

环境：Windows / Git Bash，.NET SDK `10.0.300`、Node.js `v25.9.0`。

| 检查 | 结果 |
| --- | --- |
| `dotnet build AnotherFSM.sln --nologo` | 首次被既有 `FSMNodeAnalyzerTest (32248)` 锁住输出 DLL，MSB3027/MSB3021；未杀进程或覆盖其产物 |
| `dotnet build AnotherFSM.sln --nologo --artifacts-path .scratch/spec-2-acceptance/artifacts` | 隔离产物完整构建成功，77 个警告、0 错误；含 API、WASM、分析器消费者 |
| `dotnet test AnotherFSM.sln --nologo --no-build --no-restore --artifacts-path .scratch/spec-2-acceptance/artifacts` | 核心 89/89，API 35/35，0 失败、0 跳过 |
| `dotnet .scratch/spec-2-acceptance/artifacts/bin/FSMScriptAnalyzerTest/debug/FSMScriptAnalyzerTest.dll` | PASS；真实生成消费者初始化、所有 getter、限定身份与两条执行流程断言通过 |
| `dotnet run --project .scratch/t2-toolchain/packed-consumer/Consumer.csproj --no-restore --no-launch-profile` | PASS；使用此前构建的本地 analyzer 包重跑消费者；本次未重新打包或发布包 |
| `npm run lint --prefix demos/FSMDemo.Frontend` | 通过 |
| `npm run typecheck --prefix demos/FSMDemo.Frontend` | 通过 |
| `npm run test --prefix demos/FSMDemo.Frontend` | 6/6 通过，0 跳过 |
| `npm run build --prefix demos/FSMDemo.Frontend` | 通过 |
| `git diff 06322e3...4179c6a --check` | 通过 |

本地原始日志在本目录 `*.log`，按仓库规则忽略；本报告、可复现探针和截图版本化保存。保留既有 OpenAPI NU1903、可空／未使用成员、分析器打包及 SignalR PURE 注释／大包告警，不扩大为依赖或架构重写。交互式 `FSMNodeAnalyzerTest` 本次未执行，不能混称为自动化套件通过。

## 父规格 Testing Decisions 1–14 映射

| 项 | 验收行为 | 证据／结果 |
| --- | --- | --- |
| 1 | 显式实例、属性、无运行时代理 | `ModuleDslFacts.ExplicitInstances_ConnectOrdinaryNodesAndModulesWithoutRuntimeProxies`；生成消费者；通过 |
| 2 | 普通节点进入模块、出口到普通节点／模块 | `ModuleScriptFacts`、`ModuleDslFacts`、`EngineEditingFacts`；通过 |
| 3 | 单／多／零入口及私有入口拒绝 | `ModuleDslFacts.ModuleTargets_RequireAnUnambiguousPublicInput`、`EngineEditingFacts.MultipleInputs_KeepSelectedInputIdentityInProjectionAndDeletion`；通过 |
| 4 | 非末端／多个发布者、同节点 Branch 去重 | `ModuleScriptFacts.OutputConnection_UsesActualPublishersIncludingNonTerminalInputNode`、`ModuleDslFacts.MultiplePublishers_ConnectOncePerNodeWithoutChangingBranchBindings`；通过 |
| 5 | 实例隔离、同名事件／出口、声明顺序 | `ModuleScriptFacts.ModuleInstances_*`、`OutputOrder_DoesNotChangeBranchOrConnectionIdentity`、`ModuleExportFacts`；通过 |
| 6 | 未知／私有接口、普通节点冒充、无发布者拒绝 | `ModuleScriptFacts.OutputConnection_RejectsUnknownOrPrivateInterfaces`、`ModuleDslFacts.InvalidOutputConnections_FailClearly`；通过 |
| 7 | 冲突目标／内部转移／名称碰撞拒绝 | `ModuleDslFacts.ActualNameCollisions_FailWithoutMergingObjects`、`InterfaceConnection_ConflictingWithInternalTransition_FailsWithoutRebindingBranch`、API 原子性用例；通过 |
| 8 | Namespace 与普通点号身份 | 核心／分析／React API 合同通过；**WASM 投影失败**，见下文探针 |
| 9 | 普通节点／事件／数字及命名 Branch／事件优先 | `ScriptAnalysisFacts.ReadScript_PreservesOrdinaryNodesDottedEventsAndBranchBindings`、生成 `PlainScopes`／`ATest` 的真实对象和转移断言；通过 |
| 10 | 旧 def／terminal／名称数字映射／占位源拒绝、Try 失败 | `ModuleScriptFacts.OutputMapping_*`、`ModuleLoadingFacts.MissingOrObsoleteDeclarations_HaveActionableFailures`、`ModuleDslFacts.InvalidSyntax_FailsAtPublicBuildAndTryBoundaries`；通过 |
| 11 | 同文件、注册、相对文件导入、缺失及循环错误 | `ModuleLoadingFacts`、生成消费者递归 AdditionalFiles 断言；通过 |
| 12 | 导出唯一新写法、接口去重、元数据和多入口往返 | `ModuleExportFacts`；React 真界面重新导入与截图；通过 |
| 13 | 转移重建、分析、生成编译与初始化 | `ModuleDslFacts.Transform_ClearsOldTransitionsAndRebuildsPublicOutputConnectionsWithoutReplacingNodes`、`ScriptAnalysisFacts`、消费者；通过 |
| 14 | 现有演示消费适配、普通节点行为不变 | React 闭环通过；WASM 默认脚本已迁移，但真实归属投影尚未适配完整，**部分满足** |

中英文 README、工具链集成文档、React 操作说明及 ADR-0001 已采用新 DSL；ADR-0002 未变。历史字段及停用 API 的存在本身不算旧 DSL 兼容，原测试备份也不算现行推荐示例。

## 本次 React 界面闭环

使用本次隔离构建的 API（`127.0.0.1:5079`）和真实 React 开发页面（`127.0.0.1:5174`），未直接修改 store 或用 API 代替点击操作。

1. Demo 导入，显示 CheckA、CheckB、ReviewA、公开接口和显式 Retry 目标：[01-import.png](01-import.png)。
2. 在 Checker 创建对话框仅输入 `AcceptCheck`，不设置 terminal 或出口别名：[02-create.png](02-create.png)。
3. Connect 选择 `AcceptCheck`、公开 `AcceptCheck.OK`、普通目标 `End`；接线面板核对身份：[03-wire.png](03-wire.png)。
4. Export，实际文本含 `use Checker as AcceptCheck` 属性块和 `AcceptCheck.OK -> End;`；原生可调整文本框拉高以同屏核对：[04-export.png](04-export.png)。
5. Re-import 输入与导出文本逐字比较相同后提交，恢复同一实例及接口边：[05-reimport.png](05-reimport.png)。

浏览器证据清单 5/5 已证明并 audit，无未完成项。完整结构／事件／Branch／显式元数据等价由 API 往返合同测试验证，不仅依赖截图。

## 已核实的阻塞缺口：WASM 普通点号身份

### 规格依据

- Implementation Decision 10：模块接口解析必须结合注册实例和公开接口元数据，保留普通事件名称解释，不能将所有点号表达式视为成员访问。
- Implementation Decision 18：更新必要的现有消费者、演示和数据适配，不进行无关前端重构。
- Testing Decision 8、14：合法普通点号名称保持原有解释；为迁移所必需的显示适配不改变普通节点接线行为。

### 代码与实际后果

`demos/FSMDemo.Wasm/Services/DemoRuntime.cs`：

- 第 52–57 行：仅按实例名前缀从 Root 隐藏普通节点。
- 第 35–37 行：按前缀将同一个普通节点错误显示在模块内部。
- 第 170–185 行：`CollapseModuleNodeName` 按前缀把普通节点连线折叠为模块连线。

已有真实实例 `A` 不意味着普通节点 `A.Observer` 归属于它。探针通过真实 `DemoRuntime.ImportScript` 成功构建合法脚本，但 Root 隐藏该普通节点，普通事件 `Observe` 的源错误变成模块 `A`，进入模块后又错误出现该节点。

### 复现

[wasm-probe/Probe.csproj](wasm-probe/Probe.csproj) 直接链接当前真实 `DemoRuntime.cs` 和 `BrowserNodeFactory.cs`，使用公开导入／Nodes／Connections／EnterModule 入口，无 mock、私有反射或产品代码修改。

```bash
dotnet run --project .scratch/spec-2-acceptance/wasm-probe/Probe.csproj \
  --artifacts-path .scratch/spec-2-acceptance/artifacts --no-launch-profile
```

实际输出（exit 1）：

```text
Default module projection: CheckA=[]; CheckB=[]
Root nodes: A, End, Start
Root edges: A --A.Done--> End; A --Observe--> End; Start --Launch--> A
Module A nodes: A.Entry, A.Observer, A.Retry
FAIL: ordinary dotted node A.Observer is hidden from Root and collapsed into Module A although not an expanded member.
```

期望：Root 保留 `A.Observer`，`Observe` 的源保留 `A.Observer`，模块 A 内只包含真实展开节点 `A.Entry` 和 `A.Retry`。普通 Launch 到 `A.Retry` 的入口选择在现有投影中也被压成裸实例 A；是否需要额外 UI 标记交由修复票明确，不将这一点单独当作本探针已断言失败。

默认模块 `Events=[]` 是旧 `OutputEventMap` 投影的观察值，但当前 Razor 未显示 Events，不将其单独列为已核实用户可见缺陷。不得因保留历史字段本身误报。

## 独立双轴审查

审查针对三张子票合并 diff，由两名独立 `code-reviewer` 并行完成，主代理已逐条阅读源码并核对证据。完整结果见 [review.md](review.md)。

- **Standards**：0 项文档化硬违规；1 项非阻断 Duplicated Code 建议（React store 两处图快照请求列表重复），保留为维护性建议，本次不顺手重构。
- **Spec**：1 项已核实阻塞，即 WASM 普通点号节点的真实归属投影漏项；记录到 [#6](https://github.com/Naoki326/AnotherFSM/issues/6)，未在本次验收窗口实施修复。
- 两轴均无新增无证据推测；没有因 `OutputEventMap` 历史字段本身仍存在而误报。

父规格 #2 保持 OPEN，原生 blocked-by #6（OPEN、ready-for-agent、无负责人、无前置阻塞）已回读确认。已关闭的 #3/#4/#5 不重新认领或重做。下一实现窗口只处理 #6，交付后再对 #2 复验。
