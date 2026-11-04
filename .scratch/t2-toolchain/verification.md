# T2：脚本分析与代码生成接入新模块 DSL

工单：https://github.com/Naoki326/AnotherFSM/issues/4

基线：`d9f3ba9`（T1 / #3）

## 验收边界

仅交付 #4 的消费者路径。核心 DSL 与接口解析复用 T1；图形编辑、模块感知导出和编辑器演示属于 #5，不作为本票验收项。

测试 seam：现有 `ReadScript` 回调；生成类的真实构建、`Create(factory)` 初始化、公开属性及 `Engine.GetNode/GetEvent` 查询。不是私有 Visitor 或生成文本快照测试。

## 公开脚本分析

`ReadScript` 是声明视图，不是第二个模块编译器：模块模板内部节点不作为主流程节点报告，`use` 报告实例显示元数据，出口连接报告限定 Output、实例源和保留显式 Input 的目标。接口合法性和实际发布者由 `FSMEngine` 构建入口负责。

`ScriptAnalysisFacts` 覆盖同文件模块、双实例、显示元数据、Namespace、外部模块别名及限定实例引用、普通 Branch/点号事件、与模块名称词形重叠的普通点号节点，以及不允许的 `use` 属性。

### 红绿记录

- `analysis-red.log`：首个完整新模块分析用例失败，旧路径把 ANTLR position context 的 `ToString()` 当数字，抛出 `FormatException`。
- `analysis-green.log`：实例和公开接口接线用例通过；模板不泄露为主流程节点。
- `analysis-dotted-red.log`：普通节点 `Flow.Global.Start` 因全局实例 `Global` 被误投影为 `Global.Start`。
- `analysis-dotted-green.log`：依据实际声明优先解析普通节点，4 项通过。
- `analysis-properties-red.log`：名称映射和 `Type` 属性被静默忽略，2 项失败。
- `analysis-properties-green.log`：`use` 限定为 Pos/Color/FlowID，7 项通过。

运行命令（Git Bash）：

```bash
dotnet test tests/StateMachineTest/StateMachineTest.csproj --filter FullyQualifiedName~ScriptAnalysisFacts --nologo -p:NuGetAudit=false
```

## 源生成与真实消费者

`FSMScriptAnalyzer/INTEGRATION.md` 记录生成合同和运行方式。集成程序的每条关键检查都会抛异常并以非零退出；它不依赖控制台日志作为验收断言。

- `red-inline.log` → `green-inline.log`：旧生成器把模板 `Entry` 当成主流程节点，公开 getter 查询失败；新投影隔离模板与视觉代理。
- `red-scopes.log` → `green-scopes.log`：Namespace 节点属性缺失；修正 token/scope 投影后，真实强类型 getter、所有 Branch 形式、普通点号事件及显式事件声明均通过。
- `red-embedded.log` → `green-embedded.log`：旧正则从注释伪模块名注册嵌入内容，初始化失败；改为按 import 身份注册原始文本。
- `red-relative.log` → `green-relative.log`：同名 `.fsm` 导致索引冲突；按导入文件相对路径收集依赖后，递归嵌入及别名路径通过。
- 集成断言覆盖同文件和嵌入模块、双实例隔离、多 Input 选择、多出口发布者、普通节点/事件公开属性；两条生成流程经真实 FSMExecutor 执行到结束事件。

## 双轴代码审查

两名独立 `code-reviewer` 以 `git diff --cached d9f3ba9` 审查本票改动：

### Standards

未发现有证据支持的文档规范硬违规，也未发现值得报告的 baseline smell；推测无。规范对照包括 AGENTS、领域文档与 ADR-0001。声明投影不被误当成模块引擎，未要求模块专属强类型 API。

### Spec

逐项对照 #4，缺失/部分验收、越界和实现错误均无已核实发现；推测无。父规格其他 ticket 的职责不纳入本票验收，尤其不包含 #5 图形编辑及导出。

两轴均为静态只读审查，未声称自行重跑测试。主代理确认本报告中的运行结果，并复核补充的独立 NuGet 消费工程和恢复配置。

## 最终验证

- `dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo -p:NuGetAudit=false`：89 项通过，0 失败，0 跳过（含 7 项分析合同测试）。
- `dotnet build AnotherFSM.sln --nologo -p:NuGetAudit=false`：整个解决方案构建成功，包括两个 analyzer 消费项目、API 和 WASM。
- `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj --no-build --no-launch-profile`：真实生成、初始化、公开对象及流程执行断言全部通过。
- 独立 NuGet analyzer 消费者：`.scratch/t2-toolchain/packed-consumer/Consumer.csproj` 仅通过 PackageReference 加载生成器，不通过 ProjectReference 加载生成器，全部集成断言通过；验证 Roslyn 宿主不需要额外加载 core/ANTLR 依赖。

重建独立包验证（从仓库根目录，Git Bash；不发布包到远端）：

```bash
dotnet build FSMScriptAnalyzer/FSMScriptAnalyzer.csproj -c Release -p:PackageVersion=1.1.7-t2-smoke.2 -p:NuGetAudit=false
dotnet pack FSMScriptAnalyzer/FSMScriptAnalyzer.csproj -c Release --no-build -p:PackageVersion=1.1.7-t2-smoke.2 -o .scratch/t2-toolchain/feed
dotnet restore .scratch/t2-toolchain/packed-consumer/Consumer.csproj --packages .scratch/t2-toolchain/packages --configfile .scratch/t2-toolchain/NuGet.Config -p:NuGetAudit=false
dotnet run --project .scratch/t2-toolchain/packed-consumer/Consumer.csproj --no-restore --no-launch-profile -p:NuGetAudit=false
```

恢复日志已确认实际包源为本目录 `feed` 和 `https://api.nuget.org/v3/index.json`。本机 SDK 的重复 `--source` 参数曾把 URL 解释为本地路径，改用显式 NuGet.Config 后通过；这不是代码构建失败或权限绕过。

日志和二进制产物保留于本目录，按仓库规则不纳入版本控制；本文及消费者工程保留可复现命令与结果摘要。
