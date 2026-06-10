---
name: hierarchy-interfaces
description: IFSMNode 节点继承体系、接口定义、Attribute 和工厂模式
metadata:
  type: knowledge
---

# 节点继承体系与接口

`IFSMNode` `AbstractFSMNode` `AsyncEnumFSMNode` `IFSMNodeFactory` — 节点的设计层次

## 继承体系

```
IFSMNode (接口) + ITransitionContainer + IVisualNode
    │
    ▼
AbstractFSMNode (抽象基类)
    │   实现 transition 管理、事件发布、上下文注入
    │
    ├── BaseFSMNode         ← 简单异步模式：override ExecuteAsync()
    ├── AsyncEnumFSMNode    ← 异步枚举模式：override ExecuteEnumerable()
    └── EnumFSMNode         ← 同步枚举模式：override ExecuteEnumerable()
```

## 接口定义

### IFSMNode
节点核心接口，定义执行和生命周期：
- `RunAsync()` — 执行节点
- `PublishEvent(int index)` — 按分支索引发布事件
- `Context` — `FSMNodeContext`（Token、Data 等）
- `Name` / `NamePrev` — 节点名和命名空间前缀
- `InitBeforeStart()` — 启动前初始化
- `SetBranchEvent(int index, FSMEvent evt)` — 设置分支事件绑定
- `UpdateEventDescriptions()` — 刷新事件描述（branch → EventDescriptions）
- `EventDescriptions` — 分支描述列表

### ITransitionContainer
转移容器：
- `AddTransition(event, target)` — 添加转移
- `TargetState(event)` — 获取目标节点
- `HasTransition(event)` — 是否存在转移

### IVisualNode
可视化信息：
- `PosX` / `PosY` — 坐标
- `Color` — 颜色
- `FlowID` — 流标识

## 三种执行模式

### BaseFSMNode — 简单异步
```csharp
public class MyNode : BaseFSMNode {
    protected override async Task ExecuteAsync() {
        // 一次性异步执行
        await Task.Delay(100);
        PublishEvent(FSMEnum.Next);
    }
}
```

### AsyncEnumFSMNode — 异步枚举（推荐）
```csharp
public class MyNode : AsyncEnumFSMNode {
    protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable() {
        yield return Yield.None;      // 可暂停点
        await Task.Delay(100);
        yield return Yield.Next;      // 发 Next 事件
        PublishEvent(FSMEnum.Next);
    }
}
```
每个 `yield return` 是一个暂停锚点，支持暂停/恢复/重试。

### EnumFSMNode — 同步枚举
与 AsyncEnumFSMNode 相同模式，但不支持 await。

## Attribute 体系

### FSMNodeAttribute
```csharp
[FSMNode("TypeName", "显示名", [1, 3], ["NextEvent", "BreakEvent"], Id = 5)]
```
- `TypeName` — 脚本中使用的类型名
- `Indexes` — 默认分支索引数组
- `EventDescriptions` — 默认事件描述
- `Id` — 节点类型 ID

### FSMPropertyAttribute
```csharp
[FSMProperty("显示名", isRequired, defaultValue)]
public int Count { get; set; }
```

### FSMNodeSourceAttribute
标记节点源文件信息，用于代码生成。

## IFSMNodeFactory

节点工厂接口：
- `CreateNode(string typeName)` — 按类型名创建节点
- `GetNodeType(string typeName)` — 获取节点 Type

典型实现：反射扫描带 `[FSMNode]` 的类，维护 typeName → Type 映射。

## 交叉引用

- [[core-architecture]] — nodeDict 和引擎整体
- [[instruction-system]] — yield 指令在枚举模式中的作用
- [[composition]] — GroupNode/ParallelNode 继承自 AsyncEnumFSMNode
