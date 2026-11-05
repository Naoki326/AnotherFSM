# React 双运行时开发

## 项目与入口

- `FSMDemo.Runtime`：共享 C# 图编辑、查询、模块、DSL 导入导出和执行控制。
- `FSMDemo.API`：ASP.NET Core API 与 SignalR，保留服务器节点工厂。
- `FSMDemo.Browser`：无 UI 的 .NET WASM 宿主，直接导出 JavaScript 互操作函数。
- `FSMDemo.Frontend`：唯一 React UI，以 `EditorRuntime` 接缝访问两种宿主。

默认开发配置为 `VITE_RUNTIME_MODE=api`，`VITE_API_URL` 是 API origin（默认 `http://localhost:5079`）。启动后模式固定，不做运行中迁移；非法配置或初始化失败显示错误和重试，不会回退 API。

`just dev` 同时运行 API/React，API 日志保存在 `.scratch/dev-api.log`。
`just wasm` 构建 Release 后提供完整静态编辑器，打开 `http://127.0.0.1:5180/AnotherFSM/`，不需要 API。
`just build-wasm` 仅发布到 `release/wwwroot`。其他路径设置 `VITE_BASE_PATH`（以 `/` 开头和结尾）；Windows Git Bash 使用构建脚本，避免 MSYS 将 URL 路径环境变量改写成磁盘路径。

静态服务器须正确提供 `.wasm` 的 `application/wasm` 和 JavaScript MIME。Pages 工作流打包 React 和 `_framework`，复制 `404.html` 并提供 `.nojekyll`。不再发布 Blazor 组件或引用旧应用。

## 能力矩阵

以下为原 client 的全部 **25** 个操作；其中 `getNode` 和 `getExecutionStatus` 原先仅封装，另 **23** 个已有 UI 调用。两种 Adapter 使用同一 `EditorRuntime` 合同，不把 HTTP 路由暴露给组件。

| 操作 | API | WASM | 合同场景 |
| --- | --- | --- | --- |
| getStatus | REST | 共享 C# | 导入后节点、事件与转移计数 |
| exportScript | REST 文本 | 共享 C# | 编辑后往返；无法表达的图明确拒绝 |
| getModuleInstances | REST | 共享 C# | 真实成员、公开接口、发布者、元数据 |
| getModuleDefinitions | REST | 共享 C# | Input/Output 定义 |
| createModuleInstance | REST | 共享 C# | 具名展开与名称冲突 |
| deleteModuleInstance | REST | 共享 C# | 删除实际成员，不删除同前缀普通节点 |
| updateModuleInstancePosition | REST | 共享 C# | 坐标导出与往返 |
| importScript | REST | 共享 C# | 有效替换，非法/未知节点失败保留图 |
| getNodes | REST | 共享 C# | 返回实际展开节点 |
| getNode | REST | 共享 C# | 节点详情与不存在错误 |
| createNode | REST | 共享 C# | 安全节点创建 |
| deleteNode | REST | 共享 C# | 普通节点删除与不存在错误 |
| renameNode | REST | 共享 C# | 普通节点重命名 |
| updateNodePosition | REST | 共享 C# | 布局保存与导出 |
| updateNodeEvent | REST | 共享 C# | Branch 编辑与公开 Output 不被重绑 |
| getConnections | REST | 共享 C# | 多发布者投影去重、多 Input 选择 |
| createConnection | REST | 共享 C# | 公开接口约束及失败不变更 |
| deleteConnection | REST | 共享 C# | 指定源、目标与事件删除 |
| renameConnection | REST | 共享 C# | 普通事件修改，限定出口事件拒绝重命名 |
| getExecutionStatus | REST | 共享 C# | Idle/Running/Paused/Finished 查询 |
| startExecution | REST | 共享 C# | C# 聚合绑定、缺失输入、冲突立即拒绝且原状态不变；模块 End 不提前结束 |
| pauseExecution | REST | 共享 C# | 异步节点执行时可响应 |
| continueExecution | REST | 共享 C# | 暂停后继续 |
| stopExecution | REST | 共享 C# | 停止后可再次启动，旧通知隔离 |

通知：`StateChanged`、`NodeActivated`、`NodeDeactivated`、`EngineImported`。API Adapter 封装 SignalR 与重连订阅，WASM Adapter 封装本地 C# 回调。UI 初始化、订阅与释放不依赖具体传输。

## 浏览器能力边界

浏览器安全节点由 `BrowserNodeFactory` 显式注册，包括 Start、End、Idle、Sleep、Accumulate、BusinessQuery、BusinessProcess、BusinessApproval；未知节点明确失败。GroupNode/ParallelNode 六种形态均已移除；旧 DSL（含模板子流程描述）、创建请求及旧 groupdefs 路由/WASM 操作明确失败，并提示迁向普通 Module 仅能复用定义，不等价于父子生命周期、包装出口或并行汇合。EditorRuntime、DTO 与 React 不再提供这些组合编辑能力。不支持服务器任意路径扫描、外部文件模块发现、任意动态插件或设备节点。请导入包含 Module 定义的自包含 DSL；同文件模块无需服务器文件访问。

Release 裁剪保留 DSL/节点及 DTO 所需业务反射元数据，不强制 AOT 或 Worker；平台库仍裁剪。`WasmBuildNative=false` 不需要安装原生 WASM 构建工具链。

## 业务输入与启动策略（#21）

API 与 Browser 启动代码显式安装 `DemoBusinessHost.Create()`，指定聚合类型、关系及每次新实例。依赖节点走 BindContext/Prepare，非泛型无依赖节点不制造空对象。前端只接收固定 `businessResult/businessRun` 结果 DTO，不传业务对象或 CLR 名称；双 Adapter 保持相同 start/end 接口。详见 [中英宿主配置](consumer-host.md)。

普通 start 遇到准备占用或任何未退出执行立即拒绝，不 Retire、不换绑定、不清状态或日志；停止并等待后可提供新实例。显式 Import 先验证候选装配，再等待旧执行真正退出、隔离旧通知。非法候选保留原执行。宿主来源工厂与节点初始化不持状态锁，控制不被准备阻塞。

## 验证分层

1. `just test`：核心 DSL、模块编译期展开回归。
2. `just test-api`：共享编辑/导出行为及 API 控制器回归。
3. `just test-wasm`：宿主 .NET 上的共享浏览器会话合同，**不是 WASM 兼容性证明**。
4. `npm test --prefix demos/FSMDemo.Frontend`：React 图投影与运行时配置/生命周期回归。
5. `just test-browser`：先构建 Release .NET 和 React，再在 Chrome 中使用真实 API/WASM Adapter 运行相同行为合同、UI 集成与错误场景。首次运行执行 `npx playwright install chrome`（在前端目录）；Linux CI 加 `--with-deps`。

浏览器测试先确认业务 API 未运行，在 `/AnotherFSM/` 上验证真实 WASM、监测业务 REST/SignalR 请求，再启动真实 API 执行同合同。使用事件或状态条件等待，不用固定 sleep。测试日志与失败 trace 保存在 `.scratch/issue-7/`；#21 真实逐层结果及独立工作树外的原始日志位置见 [验收记录](implementation/21-consumer-host-migration.md)；合同页仅在 `RUNTIME_CONTRACT=true` 时构建，不进入 Pages 最终产物。

最终发布使用 `bash tools/build-browser.sh`（无合同页），并检查解决方案、React 构建、子路径刷新及活动配置中无旧应用引用。
