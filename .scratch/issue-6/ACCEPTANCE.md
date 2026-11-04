# #6：WASM 真实模块成员归属修复

- 工单：[GitHub #6](https://github.com/Naoki326/AnotherFSM/issues/6)，原文快照见 [spec.md](spec.md)。
- 实现基线：`d01f42996752f364870458572d740ce1604af2d9`。
- 分支：`develop_new_module`；本窗口仅实现 #6，不接续父规格 #2 的整体验收或关票。
- 环境：Windows / Git Bash，.NET SDK `10.0.300`。

## 实现与 AC 对照

| AC | 实现／证据 |
| --- | --- |
| Root 和模块内部按真实展开成员归属 | `DemoRuntime.BuildNodes` 使用既有 `FsmModuleInstance.ExpandedNodeNames`；`RootAndModuleViews_KeepOrdinarySamePrefixNodesOutsideTheModule` 断言普通 `A.Observer` 留在 Root、不进入 A，真实 Entry/Retry 只在模块内显示，退出恢复 Root |
| 普通转移两端不误折叠，真实模块正常折叠 | `GetNodeOwner` 查询权威成员集合，`CollapseModuleNodeName` 仅折叠真实成员；模块内连接要求两端均为成员；`Connections_CollapseOnlyRealMembersAndExcludeOrdinaryNodesFromModuleView` 断言普通源、普通目标、真实入口／出口及模块内转移 |
| 核对其他前缀启发式消费点 | `ResolveExecutionEndEvents` 同样改用真实归属；执行回归断言普通 `A.Finish(End)` 正常结束、不沿 Complete 转移进入 UnexpectedEnd；真实模块的 `A.Entry(End)` 不被当成 Root 结束节点，仍通过出口进入外部 End |
| 公开边界自动回归 | 新增 `tests/FSMDemoWasmTest`，直接链接产品真实 `DemoRuntime.cs`／`BrowserNodeFactory.cs`、引用真实 DemoNodes，在宿主 .NET 上运行，无 mock、无私有反射、无测试专用产品接口；通过 ImportScript／Nodes／Connections／EnterModule／ExitModule，以及 StartAsync／Changed／Logs 验证 |
| 构建、测试、普通流程及默认演示 | 默认双实例与内部转移测试、普通脚本重新导入测试通过；项目加入解决方案及 `justfile` 的 `test-wasm` 入口；完整构建和 130 项自动化测试通过 |

未改变核心 DSL、执行器模型、Razor 布局、React、历史 `OutputEventMap`／未使用的 `Events` 投影。唯一保留的名称前缀处理是成员已经确定后的显示名截取，不再承担归属判定。

## TDD 证据

三次修复均先跑失败测试，再修改产品实现，随后单文件测试通过。日志保存在本目录（`*.log` 按仓库规则忽略）。

1. `nodes-red.log`：Root 缺少普通节点 A.Observer；`nodes-green.log`：1/1 通过。
2. `connections-red.log`：普通源被折叠成 A，普通目标间转移被吞掉；`connections-green.log`：2/2 通过。
3. `execution-red.log`：普通 A.Finish 的结束事件被漏收集，错误进入 UnexpectedEnd；`execution-green.log`：3/3 通过。
4. 加入正常行为保护测试后 `wasm-tests.log`：6/6 通过。

单文件运行：

```bash
dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --nologo
```

## 最终验证

| 命令 | 结果 |
| --- | --- |
| `dotnet build AnotherFSM.sln --nologo --artifacts-path .scratch/issue-6/artifacts -p:NuGetAudit=false` | 成功，75 个既有可空／未使用成员／分析器等警告，0 错误；含真实 WASM 项目构建，见 `build.log` |
| `dotnet test AnotherFSM.sln --nologo --no-build --no-restore --artifacts-path .scratch/issue-6/artifacts` | 核心 89/89、API 35/35、WASM 6/6；0 失败、0 跳过，见 `full-tests.log` |
| `dotnet .scratch/issue-6/artifacts/bin/FSMScriptAnalyzerTest/debug/FSMScriptAnalyzerTest.dll` | 生成消费者编译、初始化、查询集成断言 PASS，见 `script-integration.log` |
| 原父规格公开入口探针（下方命令） | exit 0，普通节点和转移恢复、模块内不泄露，见 `original-probe.log` |
| `git diff --cached --check` | 通过 |

```bash
dotnet run --project .scratch/spec-2-acceptance/wasm-probe/Probe.csproj \
  --artifacts-path .scratch/issue-6/artifacts --no-launch-profile
```

探针实际输出：

```text
Default module projection: CheckA=[]; CheckB=[]
Root nodes: A, A.Observer, End, Start
Root edges: A --A.Done--> End; A.Observer --Observe--> End; Start --Launch--> A
Module A nodes: A.Entry, A.Retry
```

`CheckA=[]`／`CheckB=[]` 是工单明确排除的历史、未被 Razor 消费的 Events 字段观察值，不作为失败。`FSMNodeAnalyzerTest` 是交互式程序，本次完整构建但未执行，不把它冒称为自动测试。未修改前端代码，不重复 React 全栈验收或截图；本票的显示数据契约由会自动失败的真实服务测试覆盖。

首次构建参数 `/p:NuGetAudit=false` 被 Git Bash 的 MSYS 参数转换改成 `p:NuGetAudit=false`，MSB1008，未进入构建；改为 `-p:NuGetAudit=false` 后完成。最终使用独立 artifacts 目录，未杀掉其他会话进程或覆盖其锁定产物。

## 双轴审查

两名独立 `code-reviewer` 并行只读审查基线到完整暂存改动，主代理核对报告与实现；审查不代替上方已实际运行的构建和测试。

### Standards

已读取完整 diff、仓库规范、词汇表与相关 ADR。硬违规：0；有充分证据、值得报告的 Fowler smell：0；推测：无。

### Spec

#6 AC 遗漏／部分实现、行为错误、范围扩张：0。已核对真实成员归属、连接两端折叠、模块内过滤及结束事件收集，6 个公开边界测试覆盖本票场景；推测：无。

两轴均通过，无需修改产品实现。父规格 #2 的复验与关闭仍留待独立窗口，不在本票上冒称整体验收完成。
