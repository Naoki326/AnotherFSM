# #7：React + .NET WASM 交付证据

工单：https://github.com/Naoki326/AnotherFSM/issues/7

实现基线：`c8ef1e969990fe2536275970a46b035a394840d6`

范围：本轮仅 #7；未纳入用户原有未跟踪的研究/规格资料。

## 实现

- 唯一 React UI，启动配置选择 API/WASM；模式、初始化加载、错误和重试可见，WASM 不回退 API。
- 从 API 提取同一份 C# EngineManager/DTO 至 `FSMDemo.Runtime`；安全节点注册和 JSON 会话供浏览器宿主使用。API 保留 REST、SignalR 和服务器节点工厂。
- `FSMDemo.Browser` 是无 Blazor UI 的 .NET WebAssembly 宿主，通过 `JSExport`/`JSImport` 与统一 `EditorRuntime` 对接。
- 旧 Blazor UI、专属 DemoRuntime、项目和活动引用已删除；有效执行/成员归属/图投影断言迁往共享会话与 React 测试。
- Pages 工作流和 `just wasm/build-wasm/test-browser` 改为 React + .NET WASM；中英文入口和 ADR-0003 同步更新。
- 原 client 的 25 操作映射见 [能力矩阵](../../docs/react-runtime.md)，两种 Adapter 运行同一组实际浏览器合同。

## 已执行的验证

环境：Windows、.NET SDK 10.0.300、.NET 浏览器运行时 10.0.9、Node.js 25.9.0、Chrome 156.0.8078.74（Playwright `chrome` channel）。

| 命令/层次 | 结果 |
| --- | --- |
| `dotnet build AnotherFSM.sln -c Release --nologo -p:NuGetAudit=false` | 通过，既有警告保留 |
| `dotnet test AnotherFSM.sln -c Release --nologo -p:NuGetAudit=false` | 144 通过：核心 92、编辑/API 35、共享浏览器会话 17 |
| `npm run typecheck --prefix demos/FSMDemo.Frontend` | 通过 |
| `npm test --prefix demos/FSMDemo.Frontend` | 10 通过 |
| `npm run lint --prefix demos/FSMDemo.Frontend` | 通过 |
| `RUNTIME_CONTRACT=true bash tools/build-browser.sh` | Release .NET WASM + React 合同产物成功 |
| `node tools/run-browser-tests.mjs` | 14 通过：真实 WASM 9、真实 API 5 |

**宿主测试不等于 WASM 兼容性证明。** 浏览器 runner 在业务 API 端口未监听时先执行 WASM 测试，每个运行时合同与完整 UI 闭环监测业务 REST/SignalR 请求；随后才启动新 API，运行相同合同。测试使用事件、状态、DOM 条件等待，不使用固定 sleep。

真实 WASM UI 闭环：导入自包含 Module 脚本 → 创建并重命名节点 → 创建/移动 Module Instance → 进入/退出模块视图 → 创建普通事件和限定 Output 连线 → 启动/暂停/继续/停止/再次运行完成 → 导出/重新导入 → 非法导入仍保留原图。

额外场景：首个 .NET 脚本请求失败后明确错误并重试成功；重复初始化、订阅取消、释放后再初始化无重复通知；停止和执行中重导入隔离旧通知；多 Input、限定 Output、多发布者去重、同前缀普通节点与 GroupNode/ParallelNode 编辑。无法表达的损坏内部状态导出拒绝由保留的 C# 公共服务回归覆盖。

## RED → GREEN 中发现的问题

1. 真实浏览器启动停留在首节点，而宿主测试不能复现：原 `FSMSyncContext` 依赖专用线程队列，单线程 WASM continuation 无法推进。改为可协作的 async 启动、移除同步等待，保留原长运行入口对服务器的支持。
2. React 丢掉首个 `NodeActivated`：执行器可能先通知节点再通知 Running，不能用 Idle 推断通知过期；过期执行器在运行时边界隔离。增加真实 UI 首节点日志/高亮回归。
3. Git Bash 把基路径环境变量改写成磁盘路径：脚本明确禁用该次 MSYS 路径转换，并验证发布 HTML 的实际资源路径。
4. 恢复执行补充 `Continued` 状态日志，经完整 UI 闭环回归验证。
5. 审查发现导入前重置 UI 导致非法导入后旧执行器仍暂停、UI 却变 Idle：改为成功后才重置；失败查询真实执行状态，保留日志、视图与选择。真实浏览器回归 RED → GREEN。
6. 审查发现同步节点主动 Pause 被 Running 覆盖：区分节点内部暂停与外部控制，保留已发生的 Paused/Finished。初始节点及转移后节点的公开会话回归均 RED → GREEN。
7. 全量测试暴露 Group 子执行器启动期停止竞态：Initialized 期间记下 pending Pause，RunAsync 安装 token 后交付，Interlocked 处理状态边界竞争，Stop/Restart 清空。异步 setup gate 固定窗口连续两次 RED（原 2 秒停止超时），修后约 206ms GREEN，未放宽超时；另保留 20 轮停止重启。真实 Release 浏览器合同也验证 Group/Parallel 各两次子流程运行与停止重启。

## 可见证据

- [暂停、继续、停止日志](evidence/execution-controls.png)
- [浏览器执行完成与节点进入/退出日志](evidence/execution-finished.png)
- [导出重新导入后的流程](evidence/script-roundtrip.png)
- [最终普通生产发布的完整模块 Demo](evidence/production-demo.png)

持久浏览器已验证 `http://127.0.0.1:5180/AnotherFSM/` 的首次加载、刷新、导入导出与执行。最终静态产物另以不包含合同页的普通发布命令复验。这里不声称 GitHub Pages 已上线；部署工作流仍由 `master` push 触发。

## 审查

### Standards

首次审查核实两项正确性问题：同步节点 Pause 被覆盖、非法导入后 UI 与真实执行状态不一致。两项均已由主 agent 核对、补充 RED → GREEN 回归并修复；独立复审确认消除，无文档标准硬违规或独立 smell 判断。后续 Group 停止竞态修正亦已独立复审，未发现新增明确问题。

### Spec

首次审查核实一项 AC3 问题：非法导入后的状态一致性（与 Standards 第二项重合）。修复后独立真实 Chrome 复验：Paused、Continue/Stop 与原日志保留；后续核心修正符合 AC5，无额外 scope，独立宿主复跑 4/4 通过。

**汇总：Standards 2 项、Spec 1 项（有重合），均修复；最终两轴剩余发现均为 0，无未核实推测作为结论。**

## 环境说明

初次 Debug 解决方案构建被先前交互式 Analyzer Demo 进程锁定程序集；未终止这一无关进程，改用 Release 构建并通过。隔离浏览器验证前，已核实并停止旧验收目录的 API；当前脚本先备份至未纳入版本控制的 `preexisting-api.fsm`。其余开发进程未触碰。
