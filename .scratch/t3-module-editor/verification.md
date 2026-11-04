# T3 / #5 模块编辑闭环验证

本窗口仅实现 [#5](https://github.com/Naoki326/AnotherFSM/issues/5)。
起点：`a5b530ac8c698e2552e8c664c6ec1444529c430f`，分支 `develop_new_module`，起始工作区干净。
依赖 #3 已关闭；#4 的脚本分析与代码生成不属于本票。

## 约定测试入口

- `EngineManager.Import` / `Export`：完整 DSL 文本往返；比较节点类型、事件身份与 Branch、实际 Transition、公开 Input/Output、显式显示元数据；不比较展开节点自动生成的 FlowID。
- `EngineManager` 公开图编辑方法：实例创建、公开接口接线、冲突与私有访问拒绝、内部模板编辑及普通节点回归。
- 现有 React 编辑入口：导入 → 创建实例 → 公开出口接线 → 导出 → 再导入。

API 使用 net10.0，核心测试使用 net8.0，故图编辑合同测试独立放在 `tests/FSMDemoApiTest/`，避免抬升核心测试运行时要求。测试复用真实轻量 `ScriptNodeFactory`，不 mock 解析器或接口解析器。

## TDD 记录

- 首条 `ModuleExportFacts.ImportExport_RoundTripsModuleInterfacesConnectionsAndDisplayMetadata` 红测：旧导出产生模块 `def`、重复出口边，遗漏实例 FlowID 及 Namespace 节点限定名。
- 后续红→绿覆盖：实例内部连接与模板不一致、逐实例 Branch 改绑拒绝导出；Namespace 中实例展开节点的 NamePrefix 往返保留。
- 图编辑服务红→绿记录见 `../t3-engine-editing-report.md`。

## 自动化与构建

| 检查 | 结果 |
| --- | --- |
| `dotnet test AnotherFSM.sln --nologo --no-restore` | 核心 89 + API 35 全通过，无跳过 |
| `dotnet build AnotherFSM.sln --nologo --no-restore` | 包含 API、WASM、分析消费者，0 错误 |
| `dotnet run --project tests/FSMScriptAnalyzerTest --no-build` | 生成消费者编译、初始化与查询 PASS |
| `npm run lint --prefix demos/FSMDemo.Frontend` | 通过 |
| `npm run typecheck --prefix demos/FSMDemo.Frontend` | 通过 |
| `npm test --prefix demos/FSMDemo.Frontend` | 6 项通过 |
| `npm run build --prefix demos/FSMDemo.Frontend` | 通过 |

保留既有警告：OpenAPI 间接依赖 `Microsoft.OpenApi 2.0.0` 的 NU1903、SignalR PURE 注释及前端大包警告。不在模块编辑票内升级依赖或重做代码分割。
额外执行的 `FSMNodeAnalyzerTest` 是交互式控制台示例：已输出“测试完成”，但其既有 `Console.ReadKey()` 在重定向 stdin 下报错；这不是 xUnit 测试套件，未将其宣称为自动化通过，也未改动无关测试程序。

## 真实界面闭环

本地 API `127.0.0.1:5079` + React `127.0.0.1:5174`，使用浏览器实际点击操作（不是直接改 store）：

1. **Demo** 导入新 DSL，显示 CheckA / CheckB / ReviewA 及公开 Input/Output，含 ReviewA.Retry 多入口接线：[01-import.png](01-import.png)。
2. 点击 **Checker → Create instance** 创建 Checker1，仅填写实例名，不设置 terminal 或出口别名：[02-create.png](02-create.png)。
3. **Connect** 选择 Checker1 → 公开 Checker1.OK → End，核对边属性：[03-wire.png](03-wire.png)。
4. **Export**，确认 `use Checker as Checker1` 与 `Checker1.OK -> End;`、位置/颜色/FlowID 同时保留：[04-export.png](04-export.png)。
5. **Re-import… → Import script**，核对输入与刚导出文本相同，恢复四个实例与相同接口边：[05-reimport.png](05-reimport.png)。
6. 创建普通 Idle，修改 Branch 为 PlainDone，再连接 End，保留普通 Rename/Delete：[06-ordinary.png](06-ordinary.png)。

浏览器清单 6/6 已 prove 并 audit。另复现并修复跨面板修改 Branch 后画布原生输入框残留旧 defaultValue：输入 key 纳入事件值，实际界面再次修改为 PlainReady 后两处字段同步。此修正不改变普通 Branch/Transition 语义。

## 审查

- Standards：无文档化标准硬违规；发现 1 个非阻断 Duplicated Code（PublicTarget 规范化重复），已合并为共享实现并重跑导出测试。
- Spec：发现 1 个 P2（Namespace 普通节点修改相对 Branch 事件后导出失败），已按 DSL 的 Namespace 规则规范化编辑输入；相对名与已限定名的往返红→绿测试覆盖，独立复核通过。
- 两轴最终均无剩余有据问题。复核结论见 [review.md](review.md)。最终全套检查在两轴修复之后重跑通过。
