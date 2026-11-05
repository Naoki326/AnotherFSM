# AnotherFSM 图形编辑演示

沿用 React + React Flow + Zustand（ADR-0002）。模块是编译期展开定义；画布上的实例是视觉代理，不是运行时节点。

## 启动与验证

默认 `VITE_RUNTIME_MODE=api`。先启动 `demos/FSMDemo.API`（HTTP `http://localhost:5079`，可用 `VITE_API_URL` 配置 origin），然后在本目录执行：

```sh
npm ci --include=dev
npm run dev
```

Windows 用 Git Bash，所有 npm 命令加 `--script-shell='C:/Program Files/Git/bin/bash.exe'`。打开 `http://127.0.0.1:5174`。显式包含 dev 依赖可避免 `NODE_ENV=production` 环境漏装 Vite/TypeScript。

```sh
npm run lint
npm run typecheck
npm test
npm run build
```

测试使用 Node 原生 TypeScript 支持（Node 22.18+ / 24+），覆盖公开接口选择、多入口身份、双实例限定事件、发布者边去重、内部模板边界、普通同前缀节点、执行高亮投影和 HTTP 请求合同。真实 API/WASM 共用合同由 Release 浏览器测试验证。

## 统一运行时

`src/runtime/types.ts` 定义 `EditorRuntime`：`initialize`、`subscribe`、`dispose` 和全部 24 项编辑/执行操作（共 27 个方法）。组件与 Zustand 只使用此合同，不直接访问 fetch、SignalR 或 .NET。

```ts
import { createApiRuntime } from './src/runtime/apiRuntime';
import { createWasmRuntime } from './src/runtime/wasmRuntime';

const runtime = createApiRuntime('http://localhost:5079');
// 或 createWasmRuntime('/AnotherFSM/')；默认使用 import.meta.env.BASE_URL。
const unsubscribe = runtime.subscribe(event => console.log(event));
await runtime.initialize();
await runtime.getNodes();
unsubscribe();
await runtime.dispose();
```

启动时设置 `VITE_RUNTIME_MODE=wasm` 可选择浏览器本地运行，发布目录须包含 `FSMDemo.Browser` 的 `_framework/` 产物。页面顶部始终显示当前模式和初始化 loading/error/retry；WASM 失败不会回退 API。API 模式的 SignalR 建连/订阅失败同样作为初始化错误显示。初始化成功后点击原有 **Demo** 入口加载 `src/demo.fsm`。

WASM 保留已创建的 .NET runtime，`dispose` 只释放 session；生命周期调用串行化以兼容 React StrictMode。图刷新与导入有失效保护，停止/导入会取消旧高亮定时器；刷新失败在页面显示并可重试。

## C# 业务输入与启动失败

API/WASM 均由 C# `DemoBusinessHost` 显式配置 `DemoBusinessInput` 及每次实例；可导入 `demos/BusinessContext.fsm` 执行业务示例。前端不配置任意 JSON/CLR 依赖，只接收固定结果 DTO。普通 start 遇到未退出执行立即失败，不自动停止旧执行、不清除原状态/日志；显式 Stop 等待后才启动新执行。Import 是单独的显式替换，先验证候选，再等旧执行真实退出。详细双语配置见 [宿主说明](../../docs/consumer-host.md)，整个无依赖引擎使用 `engine.Prepare()` 而非空输入或旧构造/Restart；旧 Context.Data 包装已删除。真实 Release WASM/API 证据见[最终记录](../../docs/implementation/22-final-acceptance.md)。

## 导入 → 创建实例 → 接线 → 导出 → 再导入

1. 点击 **Demo**，加载 `src/demo.fsm`；或者点击 **Import**，粘贴多行新 DSL / 选择 `.fsm` 文件，再点 **Import script**。成功导入才替换当前图；解析错误或旧组合用法被拒绝时保留原图，并在对话框中显示诊断。
2. 默认图含 `CheckA`、`CheckB`（相同模块、事件隔离），以及有 `Entry` / `Retry` 两个 Input 的 `ReviewA`。`CheckB.OK → ReviewA.Retry` 明确保存入口身份。`ReviewA.OK` 有两个真实发布者，但只显示一个公开接口边。
3. 点击左侧 **Checker**，在 **Create Module Instance** 对话框输入 `Checker1`（可使用 Namespace 前缀，如 `Work.CheckC`），点击 **Create instance**。也可拖拽模块到 Root 自动生成不冲突名称。创建无需配置任何出口别名。
4. 点击画布左上 **Connect**，选 Source=`Checker1`、Public Output=`OK — Checker1.OK`、Target=`End`，点击 **Connect**。也可从实例右侧命名 Output 句柄拖到目标 Input 句柄，弹窗会预选该接口；普通源节点可选择已有 Branch 事件或输入普通事件名。
5. 普通节点进入实例：选择普通节点及其事件，Target 选择单入口实例（例如 `Checker1 (Input: Entry)`）。多入口实例只能选择 `ReviewA.Entry` / `ReviewA.Retry`，没有猜测第一个 Input 的选项。实例间串联同理。
6. 若要把新实例串入默认执行链，先选中并 **Delete** 原有 `CheckA.OK → CheckB` 边，再连接 `CheckA.OK → Checker1` 和 `Checker1.OK → CheckB`。一个源事件已连接其他目标时，服务会明确拒绝冲突，不自动覆盖。步骤 4 若已连接到 End，应先删除那条边。
7. 拖动实例保存位置；选中实例，在属性面板查看公开接口、限定事件、真实发布者、Color / FlowID。实例 Output 及其外部边不提供事件重命名或映射控件。
8. 点击 **Export** 查看模块感知脚本，可复制或 **Download .fsm**。点击 **Re-import…** 再 **Import script**，或通过 Import 选取下载文件。应恢复相同公开接口、分支绑定、转移、Namespace、多入口目标和已保存的显示元数据。不能用新合同表达的历史图会明确报错，不会静默改写事件。

## 普通节点与内部模板

- Root 中保留普通节点的点击/拖放创建、移动、选择、重命名、Branch 事件编辑、接线、删除；普通事件边仍可 Rename / Delete。
- Namespace 内普通节点编辑 Branch 时，相对事件名保留当前命名范围（如 `N.Start` 中的 `Other` 保存为 `N.Other`）；已限定的 `N.Other` 不会重复添加前缀。
- 选中实例点 **Enter**，或右键 **Enter Module**，进入内部模板画布。可编辑内部位置、Branch 事件和内部连接，修改作用于该模块的全部实例。点击面包屑 **Root** 返回外部画布。
- 内部画布的接线选择器只列出该实例内部节点；外部画布只列出公开 Input/Output，不能绕接口接到私有内部节点。新增/删除/重命名模块模板节点及接口声明通过脚本 Import 完成，不提供不能持久化的伪操作。
- 不需要、也不提供模块 terminal 配置、实例出口外部别名或实例出口重命名。新实例使用 `use Checker as CheckA { Pos:(310,100); Color:"orange"; FlowID:check_a; }`，出口直接使用 `CheckA.OK->Next;`。
- 默认 **Start** 按 `Start` / `EndEvent` 启动执行；导入其他脚本时需保留这两个演示执行入口名称，编辑/导入导出本身不依赖它们。

## 旧运行时组合已移除（#17）

GroupNode / ParallelNode 的非泛型、单泛型、双泛型共六种形态及其编辑入口已移除，统一运行时不再提供 `updateGroupDefs`。旧类型键及 `[StartNode->EndEvent];` 子流程描述（包括 Module 模板内）在导入/装配时明确拒绝；旧 API/WASM 组合请求也不能成功。

纯定义封装改用普通 Module 的 Input/Output 和真实 Branch。迁移**非等价**：Module 只做编译期展平，不自动保留父子生命周期、包装出口或并行汇合；不提供 ParallelModule 或静默串行替代。详见[根目录迁移说明](../../README.md)。
