# #21 消费者与宿主迁移 / Consumer-host migration

## 范围与基线

分支 `pi-web-agent-d1937fe7-ef0e-48d3-a545-869d89c81b2f`；唯一工作树 `C:/Work/AnotherFSM-worktrees/pi-web-agent-d1937fe7-ef0e-48d3-a545-869d89c81b2f`。
开工 status 干净；HEAD 与 merge-base 都为 `4063178bca93749ce966988920d349852e80108f`。
仅实施 #21，不委派、不启动模型 CLI/代理、不建第二工作树、不改变 #20 分支、不 push/关闭票。

生产配置及双语使用说明：[中文](../consumer-host.md) / [English](../consumer-host.EN.md)。
原始日志必须另存 `C:/work/anotherfsm-context-19/evidence-21/`，不依赖此隔离工作树生命周期；最终结果和校验清单将在合并最新 #20 集成 tip 后补全。

## 阶段提交（按验收族）

- `451458c`：多结束事件与首节点前订阅的最小公开兼容扩展；原 #19 单结束事件调用形状保留。
- `f4d0097`：普通 start 不再 Retire 旧执行。
- `17041c3`：C# 显式聚合配对、真实业务节点、每轮输入、API/Browser 配置及固定结果 DTO。
- `5d31a0e`：真实工厂创建错误保留节点身份/根因。
- `0fad971`：类型化启动支持宿主独立结束事件，不为失败 start 改图。
- `20ef949`：直接 C#/Fluent/真实 DSL/Module、三种实际工厂和完整来源集成。
- `36bb1cf`：宿主准备/初始化不持状态锁；普通 start 立即拒绝；显式替换等待准备与真退出。
- `accba30` / `e6fe50b`：只读核心占用快照及宿主在请求输入前拒绝已有准备。
- `b7e096b`：Demo 控制入口、生成消费者业务执行、真实编译正负例。
- `0a69c4d`：失败 start 不清 UI 日志，同 EditorRuntime 的真实 Release WASM/API 业务合同。
- `ca2112a` / `4bffb85`：会话准备冲突、无效候选保留暂停执行、生成类型访问器真实编译/查询/执行。
- `aafa6b2` / `e5a0680`：Finished 未真退出回归；初版测试误等尚未请求的取消清理，改为先观察 Finished 再显式取消，未修改核心退出语义。
- `f2fabd8`：Finished 不是退出证明，UI 仍允许显式 Stop。

## 合入 #20 前的真实 red → green

所有接缝由父规格预先确认：公开核心装配/执行、同 EditorRuntime API/真实 Release WASM、真实 C# 编译器。未另行组织独立审查。

| 行为 | Red（原始日志） | Green |
| --- | --- | --- |
| 类型化消费者保留多个结束事件并订阅首节点 | `core-extension-red.log`：CS1503/CS1660 | `core-extension-green.log`：核心选集 79 通过 |
| 普通 start 不得隐式替换旧执行 | `start-red.log`：第二次启动成功、没有 error | `start-green.log`：宿主 .NET 27 通过 |
| 显式 C# 宿主配对及每轮实例 | `typed-host-red.log`：缺宿主配置/结果 DTO 的编译错误 | `typed-host-green.log`：生产 scanning 工厂真实业务结果 42 |
| 错误类型键保节点限定名与根因 | `factory-diagnostic-red.log`：直接 C#/DSL 两例失败 | `factory-diagnostic-green.log`：4 通过 |
| 已有准备时失败 start 不增添结束事件 | `prepared-host-red.log`：事件集合被改变 | `prepared-host-green.log`：15 通过 |
| 准备中立即拒绝第二 start，Stop 等待准备 | `host-preparing-red.log`：第二 start 被宿主锁阻塞，超时 | `host-preparing-green.log`：API 55 通过；session 27 通过 |
| 外部准备占用时不提前请求本轮来源 | `occupied-input-red.log`：来源 provider 被调用 | `occupied-input-green.log`：宿主选集 6 通过 |
| 生成业务流程及类型化节点/事件访问器 | `generated-red.log`、`generated-accessor-red.log`：真实编译缺失生成类型/访问器 | `generated-green.log`、`generated-accessor-green.log`：编译、查询、执行成功 |
| 失败 start 保留 UI 旧日志 | `browser-red-2/wasm-results.json`：11 过、1 失败及 trace | `browser-green/`：WASM 12/API 7 通过 |
| Finished 仍提供显式 Stop | `finished-ui-red/wasm-results.json`：11 过、1 失败及 trace | `browser-final-2/`：WASM 12/API 7 通过；合并前最后联合命令工具报 120s 超时，结果 JSON 和 runner success 已落盘，随后核验 5079/5180 均关闭，最终合并后仍须独立重跑 |

`consumers.log` 的三次失败来自测试 fixture 使用错误的 Namespace 大小写和文件模块缺 wrapper；修正真实 DSL 后 `consumers-green.log` 10 通过，未伪称生产缺陷红灯。

## 合并前验证边界

`solution-final-test-2.log`：核心 232、API 56、宿主 .NET 30，全过。`solution-final-release-2.log`：Release solution 0 错误、62 警告。`compiler-final.log`：正例和 8 个真实编译负例，包括生成消费者 CS1503。前端 test 12、typecheck/lint 均过。以上不是 #20 合并后的最终验收，合并后另记。

环境实际问题及处置：初次 npm ci 受机器 omit=dev 配置影响，改用 `npm --script-shell='C:/Program Files/Git/bin/bash.exe' ci --include=dev`；继承 PORT=30141 与 runner 默认不同，显式 `PORT=5180`，未杀已有服务。JRE 为 `C:/Users/Naoki/.jre/jdk-11.0.26+4-jre`。Release browser 由 `RUNTIME_CONTRACT=true bash tools/build-browser.sh` 构建，再由 `PORT=5180 node tools/run-browser-tests.mjs` 真实 Chrome 执行。宿主 .NET 从未冒充浏览器。

现有依赖安装报告 18 个 npm vulnerabilities（11 moderate、7 high）；未在消费者票擅自升级 lock。已有反射/Reactive 裁剪、nullable/打包警告保留，真实 Release WASM 结果而非警告推断作为兼容证据。
