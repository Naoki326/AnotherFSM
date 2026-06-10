# ADR-0002: 前端从 Blazor 迁移至 React

- **日期**: 2026-06-02
- **状态**: 已决策
- **决策者**: naokiz

## 背景

当前前端基于 Blazor（Razor 组件）+ vendor Drawflow JS 库。Drawflow 存在容器尺寸计算错误、初始视图不居中、复杂连线混乱等问题，且 2400 行 vendor JS 难以维护。Masa.Blazor 已在先前提交中移除。综合考虑生态成熟度和可维护性，决定将前端迁移至 React 技术栈。

## 决策

1. **React + Vite + TypeScript** 替代 Blazor，作为独立 `frontend/` 目录（monorepo）
2. **React Flow + dagre** 替代 Drawflow，提供自动布局能力
3. **Ant Design** 作为 UI 组件库
4. **Zustand** 作为状态管理（React Flow 官方推荐搭配）
5. **ASP.NET Core Web API + REST/JSON** 替代 Blazor 直连 C# 内存对象
6. **SignalR (WebSocket)** 实现执行器运行时节点状态实时推送
7. **WPF + WebView2** 保留桌面端，加载 React 前端
8. **渐进迁移**：先建 API 层，逐页替换现有 Blazor 组件

## 替代方案与取舍

| 方面 | 选中方案 | 替代方案 |
|------|---------|---------|
| 前端框架 | React | 修修补补 Blazor + Drawflow，成本低但治标不治本 |
| 流程图库 | React Flow + dagre | 手写 SVG、Cytoscape.js、Mermaid |
| 组件库 | Ant Design | MUI（Material 风格近 Masa.Blazor）、Tailwind CSS + shadcn/ui |
| 状态管理 | Zustand | Redux Toolkit、Jotai、React Context |
| 实时通信 | SignalR | 轮询、SSE |
| 桌面方案 | WPF + WebView2 | Electron、仅保留 Web |
| 迁移策略 | 渐进迁移 | 一次性重写 |
