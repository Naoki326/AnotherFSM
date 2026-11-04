## Parent

Part of #2 — 新模块 DSL 父规格整体验收发现的消费者适配漏项。

## 问题

WASM 演示的默认脚本已迁移至新 DSL，但 `DemoRuntime` 仍仅凭 `实例名.` 字符串前缀判断普通节点归属，而核心及 React 图编辑已按真实展开成员解析。

合法脚本同时包含 `use M as A;` 和普通节点 `def A.Observer(Idle) { 1->Observe; }` 时，WASM Root 隐藏 `A.Observer`，把普通 `Observe -> A.Observer to End;` 错误画成模块 A 到 End；进入模块 A 后又错误显示这个不属于模块的普通节点。

涉及 `demos/FSMDemo.Wasm/Services/DemoRuntime.cs` 的 `BuildNodes`（35–57 行）、`BuildConnections`／`CollapseModuleNodeName`（155–185 行）。同文件其他按前缀判断成员的消费点应核对，不能只修当前示例。

依据父规格 Implementation Decisions 10、18 及 Testing Decisions 8、14：普通点号名称按真实身份解析，必要演示显示适配不得改变普通节点连接。

## 可复现证据

父规格整体验收目录 `.scratch/spec-2-acceptance/`，探针 `wasm-probe/Probe.csproj` 直接链接产品真实 DemoRuntime／BrowserNodeFactory，以公开入口运行，无 mock 或私有反射。

```bash
dotnet run --project .scratch/spec-2-acceptance/wasm-probe/Probe.csproj \
  --artifacts-path .scratch/spec-2-acceptance/artifacts --no-launch-profile
```

验收代码 `4179c6a` 上 exit 1：

```text
Root nodes: A, End, Start
Root edges: A --A.Done--> End; A --Observe--> End; Start --Launch--> A
Module A nodes: A.Entry, A.Observer, A.Retry
FAIL: ordinary dotted node A.Observer is hidden from Root and collapsed into Module A although not an expanded member.
```

## Acceptance criteria

- [ ] WASM Root 及模块内部视图按实例真实展开成员判断归属，不仅凭名称前缀；与实例同前缀但非成员的普通点号节点保留在 Root，不泄露到模块内部。
- [ ] 对应普通事件转移的真实源／目标不被错误折叠为模块；真实模块内部节点的视觉折叠和默认新 DSL 演示保持可用。
- [ ] 核对 DemoRuntime 内相同前缀启发式的其他消费点（例如执行结束事件收集），采用真实成员身份，避免修一处漏一处；不新增另一套接口语义。
- [ ] 在现有 DemoRuntime 公开 ImportScript／Nodes／Connections／EnterModule 边界补充可自动失败的回归验证，覆盖同前缀普通节点与真实展开节点共存；不只依赖截图。
- [ ] 相关构建与测试通过；普通节点、模块默认演示不回归。不扩展为 WASM UI 重做、运行时模型变更或历史公共字段全面清理。

## 边界与阻塞

核心 DSL、源生成器、React 编辑器／模块感知导出在本次验收均通过，不重复重做已关闭 #3/#4/#5。上述历史 Events 空数组未被 Razor 消费，不仅因 OutputEventMap 字段仍存在就判成兼容缺陷。

无未完成前置依赖；这是后续独立实现票。本次父规格验收不实现此票，#2 在本票交付并复验前保持开启。
