# ADR-0003：同一 React 编辑器承载 API 与浏览器运行模式

- **状态**：已决策
- **关联工单**：[#7](https://github.com/Naoki326/AnotherFSM/issues/7)

保留 ADR-0002 的 React UI、REST/SignalR 远程运行方式，新增启动时明确选择的浏览器本地模式。删除独立 Blazor UI，而不是并存第三个应用：React 的统一运行时接口收敛编辑操作、初始化、订阅和释放；API Adapter 封装网络，WASM Adapter 通过 .NET JavaScript 互操作调用同一共享 C# 编辑与执行逻辑。

## 边界与取舍

- `FSMDemo.Runtime` 从 API 中提取编辑、查询、导入导出与执行行为，不依赖 ASP.NET Core。API 保留服务器节点发现；浏览器使用显式安全节点注册，拒绝外部文件模块发现与未知节点。
- `FSMDemo.Browser` 使用 .NET WebAssembly SDK 与 `JSExport`/`JSImport`，没有 Razor、Blazor 组件树或另一套演示运行时。React 与浏览器宿主通过可序列化的 DTO、错误和事件通信，不传递可变 .NET 对象。
- 按启动配置选择 `api`/`wasm`，不实现运行中无损切换。WASM 初始化错误显示重试，不回退到远程 API；静态发布默认 WASM。
- 生命周期由统一运行时接口管理，停止、成功重导入和释放隔离旧执行器通知。执行任务不能阻塞互操作入口，否则单线程浏览器无法响应暂停和停止。
- Release 保留 DSL 节点/属性及图 DTO 所需的业务程序集反射元数据，同时裁剪平台库。不强制 AOT、Worker 或多线程；是否可运行以真实浏览器中的 Release 产物为准，宿主 .NET 测试不能替代它。
- ADR-0001 与领域词汇保持不变：Module 是编译期复用单元，Input 是节点，Output 是事件，Module Instance 视觉代理不执行；成员关系来自实际归属，不能按名称前缀猜测。

## 验证接缝

用户确认以 React 使用的统一运行时接口进行 API/真实 WASM 同合同测试，保留共享 C# 编辑与模块回归、React 图投影测试，并在项目子路径上对发布产物执行浏览器闭环与网络隔离验证。
