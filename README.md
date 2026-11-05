<h1 align="center">AnotherFSM</h1>

<p align="center">基于有限状态机快速构建流程的工具</p>

### Wiki
[![zread](https://img.shields.io/badge/Ask_Zread-_.svg?style=flat&color=00b0aa&labelColor=000000&logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB3aWR0aD0iMTYiIGhlaWdodD0iMTYiIHZpZXdCb3g9IjAgMCAxNiAxNiIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHBhdGggZD0iTTQuOTYxNTYgMS42MDAxSDIuMjQxNTZDMS44ODgxIDEuNjAwMSAxLjYwMTU2IDEuODg2NjQgMS42MDE1NiAyLjI0MDFWNC45NjAxQzEuNjAxNTYgNS4zMTM1NiAxLjg4ODEgNS42MDAxIDIuMjQxNTYgNS42MDAxSDQuOTYxNTYONS4zMTUwMiA1LjYwMDEgNS42MDE1NiA1LjMxMzU2IDUuNjAxNTYgNC45NjAxVjIuMjQwMUM1LjYwMTU2IDEuODg2NjQgNS4zMTUwMiAxLjYwMDEgNC45NjE1NiAxLjYwMDFaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00Ljk2MTU2IDEwLjM5OTlIMi4yNDE1NkMxLjg4ODEgMTAuMzk5OSAxLjYwMTU2IDEwLjY4NjQgMS42MDE1NiAxMS4wMzk5VjEzLjc1OTlDMS42MDE1NiAxNC4xMTM0IDEuODg4MSAxNC4zOTk5IDIuMjQxNTYgMTQuMzk5OUg0Ljk2MTU2QzUuMzE1MDIgMTQuMzk5OSA1LjYwMTU2IDE0LjExMzQgNS42MDE1NiAxMy43NTk5VjExLjAzOTlDNS42MDE1NiAxMC42ODY0IDUuMzE1MDIgMTAuMzk5OSA0Ljk2MTU2IDEwLjM5OTlaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik0xMy43NTg0IDEuNjAwMUgxMS4wMzg0QzEwLjY4NSAxLjYwMDEgMTAuMzk4NCAxLjg4NjY0IDEwLjM5ODQgMi4yNDAxVjQuOTYwMUMxMC4zOTg0IDUuMzEzNTYgMTAuNjg1IDUuNjAwMSAxMS4wMzg0IDUuNjAwMUgxMy43NTg0QzE0LjExMTkgNS42MDAxIDE0LjM5ODQgNS4zMTM1NiAxNC4zOTg0IDQuOTYwMVYyLjI0MDFDMTQuMzk4NCAxLjg4NjY0IDE0LjExMTkgMS42MDAxIDEzLjc1ODQgMS42MDAxWiIgZmlsbD0iI2ZmZiIvPgo8cGF0aCBkPSJNNCAxMkwxMiA0TDQgMTJaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00IDEyTDEyIDQiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIxLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPgo8L3N2Zz4K&logoColor=ffffff)](https://zread.ai/Naoki326/AnotherFSM)

简体中文 | [English](./README.EN.md)

### Demo

[演示流程的控制、修改、执行](https://naoki326.github.io/AnotherFSM)

### 介绍

AnotherFSM 是一个**基于有限状态机、快速构建流程的工具库**，不同于常见的工作流引擎，它的工作流部分仅仅基于有限状态机，除了节点、事件，没有定义其他特别的结构

与通常的状态机不同的地方在于：通常有限状态机的节点仅仅表示状态(State)，另有一个动作(Action)的概念以执行相应操作。而本工具在跳转到某一状态节点上时会自动执行该节点对应类的执行代码，也就是将动作(Action)和状态(State)结合在一起，是简化的状态机

另外推出了一种状态机DSL，用于快速构建状态机流程图。DSL设计参考了Martin Fowler的著作，实现则基于Antlr4

### 项目结构

| 项目 | 说明 |
| --- | --- |
| **StateMachine** | 核心库，基于 .NET Standard 2.0，功能完整可独立使用 |
| **FSMDemo.API** | Web 可视化编辑器后端，ASP.NET Core Web API + SignalR |
| **FSMDemo.Frontend** | Web 可视化编辑器前端，React + xyflow + Ant Design |
| **FSMDemo.Runtime** | API 与浏览器共用的 C# 图编辑、DSL 导入导出和执行行为 |
| **FSMDemo.Browser** | 无 Blazor UI 的 .NET WASM 宿主，供同一 React 编辑器在浏览器内运行 |
| **DemoNodes** | 演示用自定义节点集合 |
| **FSMScriptAnalyzer** / **FSMNodeAnalyzer** | 基于 Antlr4 的脚本/节点分析器 |
| **FSMScriptAnalyzerTest** / **FSMNodeAnalyzerTest** | 分析器测试项目 |

### 依赖项

- **StateMachine** 项目基于 **.NET Standard 2.0**，可独立使用，无界面依赖
- **FSMDemo.API** 基于 **.NET 10**，提供 RESTful API 和 SignalR 实时通信
- **FSMDemo.Frontend** 基于 React + [xyflow](https://github.com/xyflow/xyflow) + [Ant Design](https://ant.design/) 构建，使用 [dagre](https://github.com/dagrejs/dagre) 自动布局，[zustand](https://github.com/pmndrs/zustand) 管理状态，提供流程图可视化编辑与执行控制界面
- **FSMDemo.Browser** 使用 **.NET 10 WebAssembly** 与 JavaScript 互操作，在浏览器内执行 C#，无需业务后端；旧 Blazor 应用已删除。两种模式共用同一 React UI，见 [ADR-0003](docs/adr/0003-react-browser-runtime.md)。

### 引用

1. 脚本语言处理部分使用了工具 Antlr4[^antlr4] 生成语法解析代码

[^antlr4]: Antlr4 是一个语法解析生成工具 项目链接 [Antlr4](https://github.com/antlr/antlr4)

2. 前端流程图编辑器基于 [xyflow](https://github.com/xyflow/xyflow)[^xyflow]（React Flow）构建

[^xyflow]: xyflow 是一个开源的 React 流程图/节点编辑器库 项目链接 [xyflow](https://github.com/xyflow/xyflow)

### 快速启动

使用 [just](https://github.com/casey/just) 命令运行：

```bash
# 同时启动 API 后端和前端开发服务器
just dev

# 或分别启动
just api        # 启动 API 服务（端口 5079）
just frontend   # 启动前端开发服务器（端口 5174）

# 完整 React WASM 编辑器（无需业务后端）
just wasm       # 构建 Release 并在端口 5180 提供静态站点
just build-wasm # 发布到 release/wwwroot，用于 GitHub Pages
just test-browser # API/真实 WASM 的浏览器合同及 UI 回归
```

前置条件：.NET 10 SDK、Node.js 22.18+（或 24+）、npm、JDK（ANTLR 构建使用）。首次运行前执行 `npm ci --prefix demos/FSMDemo.Frontend`。Windows 命令使用 Git Bash。

- API 开发默认模式为 `api`；`VITE_API_URL` 可配置 API origin，默认 `http://localhost:5079`。
- `just build-wasm` 固定选择 `wasm`，默认基路径 `/AnotherFSM/`；打开 `http://127.0.0.1:5180/AnotherFSM/`。其他托管路径通过 `VITE_BASE_PATH` 配置，须以 `/` 开头和结尾。
- 浏览器只支持自包含 DSL 与其中的 Module 定义，以及安全内置/Demo 节点。服务器文件系统模块发现、动态插件、设备节点不受支持，会明确报错。
- 构建、合同覆盖和浏览器验证方法见 [运行时开发说明](docs/react-runtime.md)。

### 简单使用教程（仅 StateMachine 项目）

这里有一个简单的例子：[上手项目](https://github.com/Naoki326/AnoterFSM.Demo)

本工程设计的状态机，其运行基于两个重要类：FSMEngine、FSMExecutor

- ***FSMEngine***

该类型负责保存一个整体状态图结构，一个 FSMEngine 对象内部包括若干节点、事件及节点与节点之间通过事件相连接的关系。FSMEngine 包含创建、构建、改变状态机的图结构的一系列 API，另外还包含通过传入脚本的方式构建图结构的 API（该脚本是本工程设计的一种描述状态机的自定义 DSL 语言）

- ***FSMExecutor***

每个 FSMExecutor 实例管理一个执行状态机的对象，该对象对应一个状态机的执行线程，可以控制、监控状态机的执行

- ***自定义节点***

当状态机执行时，进入到节点中，将会调用对应的节点对象内的相应方法，并在方法执行完成后才能跳入下一个节点。这些节点是使用者自定义的节点，但这些自定义节点类需要继承本工程提供的一系列的节点基类，并按照一定的规则来编写代码。同时，继承这些类型，可以在代码编写环境中获得节点执行的上下文环境

#### 1. IFSMNodeFactory

这个接口是 FSMEngine 构造节点对象时调用的节点工厂类，需要使用者自行实现。在调用 FSMEngine 构造节点的方法时，传入的是节点类型的 Key 值，此时会调用到该工厂类来创建对应的节点对象。考虑到使用者可能会采用不同的方式构造对象（如反射创建、容器创建等），将节点工厂类的实现交给使用者自行定义。

##### 基于反射的节点工厂

Demo 项目提供了一个基于反射的实现 `ReflectionNodeFactory`，无需 IoC 容器即可使用：

```csharp
var factory = new ReflectionNodeFactory(Assembly.GetExecutingAssembly());
var engine = new FSMEngine(factory);
```

##### 基于 Autofac 的 IoC 配置

首先，实现接口 IFSMNodeFactory：

```csharp
public class AutofacNodeFactory : IFSMNodeFactory
{
    private ILifetimeScope container;

    public AutofacNodeFactory(ILifetimeScope container)
    {
        this.container = container;
    }

    public IFSMNode CreateNode(string name)
    {
        return container.ResolveKeyed<IFSMNode>(name);
    }

    public Type GetNodeType(string name)
    {
        var registration = container.ComponentRegistry.Registrations
            .FirstOrDefault(r =>
                r.Services.OfType<KeyedService>().Any(s =>
                    s.ServiceKey.Equals(name) && s.ServiceType == typeof(IFSMNode)));

        if (registration != null)
            return registration.Activator.LimitType;

        throw new InvalidOperationException($"No IFSMNode service with key '{name}' found.");
    }

    public IEnumerable<Type> GetNodeTypes()
    {
        return container.ComponentRegistry.Registrations
            .SelectMany(r =>
                r.Services.OfType<KeyedService>().Where(s =>
                    s.ServiceType == typeof(IFSMNode))
                .Select(s => r.Activator.LimitType))
            .Distinct();
    }
}
```

然后在自定义节点所在项目中注册 Autofac Module：

```csharp
internal class YourModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
            .AssignableTo<IFSMNode>()
            .As(t =>
            {
                string? key = (t.GetCustomAttribute(typeof(FSMNodeAttribute)) as FSMNodeAttribute)?.Key;
                if (key is not null)
                    return new Autofac.Core.KeyedService(key, typeof(IFSMNode));
                throw new InvalidOperationException("DeviceImplInject key has not set!");
            })
            .InstancePerDependency();

        base.Load(builder);
    }
}
```

#### 2. 流程结构管理类

- ***FSMEngine***

| 常用函数 | 描述 |
| --- | --- |
| CreateNode | 创建一个节点，需要输入节点类型和节点名称 |
| ReinitGroupNode | 向所有 IFSMEngineInjectable 节点注入引擎（历史方法名；需在 CreateNode 之后调用） |
| ConnectNode | 创建节点之间的连线（事件名、出节点、入节点） |
| ChangeNodeName | 修改节点名称 |
| ChangeTransitionName | 修改连线名称 |
| DeleteTransition | 删除一条节点连线 |
| ClearTransition | 删除当前节点的所有连线 |
| AttachEvent | 为当前节点添加可发出的事件 |

- 通过脚本构建/重组：

| 常用函数 | 描述 |
| --- | --- |
| CreateStateMachine | 通过脚本创建 FSMEngine |
| CreateStateMachineByFile | 通过脚本文件创建 FSMEngine |
| Transform | 对当前状态图进行重组（连线改变，已有节点属性不变） |
| TransformByFile | 通过脚本文件重组 |
| ToString | 将当前状态图输出为脚本 |

- ***FSMEngineBuilder***（Fluent API）

```csharp
var engine = FSMEngineBuilder.Create()
    .ConfigureNodeFactory(new AutofacNodeFactory())
    .ConfigureFSMDefine(build =>
    {
        build.AddNode<SleepNode>("Sleep")
            .AddConnection("NextEvent", "Start", "Sleep")
            .AddNode("Sleep2", "SleepNodeKey");
    })
    .Build();
```

#### 3. 流程执行类

- ***FSMExecutor***

每个 FSMExecutor 实例管理一个执行状态机的对象，可以控制、监控状态机的执行。

> 状态流将在 Task 中执行，若流程出现异常，Task 自动退出。对于流程出现的任何异常，可检查 IObservable 接口的 OnError 或者 NodeExceptionEvent 事件。

| 函数 | 描述 |
| --- | --- |
| FSMExecutor | 构造函数，需要传入启动节点与完成事件 |
| RestartAsync | 重启动（自动调用停止，等待前次执行正确退出） |
| PauseAsync | 暂停（等待当前执行节点正确暂停） |
| Continue | 继续（从暂停位置恢复，同步方法） |
| StopAsync | 停止（等待当前节点阻塞操作完成） |

- 监控相关

| 接口 | 类型 | 描述 |
| --- | --- | --- |
| State | 属性 | 当前流程执行的状态 |
| FSMStateChanged | 事件 | 流程执行状态改变事件 |
| IObservable | 接口 | 订阅所有节点相关事件（进入、退出、暂停、继续、错误等） |
| NodeStateChanged | 事件 | 节点进入事件 |
| NodeExitChanged | 事件 | 节点退出事件 |
| NodeExceptionEvent | 事件 | 节点抛出异常事件 |
| TrackStateEvent | 事件 | 传递 IObservable 接口发出的所有事件 |
| TrackCallEvent | 事件 | 流程控制方法调用时发出（RestartAsync、PauseAsync、Continue、StopAsync） |

#### 4. 节点基类

自定义的节点类需要继承节点基类来编写，可实现跳转到该节点时执行相对应的节点代码。

| 类 | 描述 |
| --- | --- |
| AbstractFSMNode | 节点的初始抽象类，继承该类的任意类型都可以作为节点使用 |
| SimpleFSMNode | 最基础的节点类型，只需实现 ExecuteMethodAsync 方法 |
| EnumFSMNode | 基于 C# yield 机制实现了局部暂停继续的节点，需实现 ExecuteEnumerable 返回 IEnumerable\<object\> |
| AsyncEnumFSMNode | 同 EnumFSMNode，增加异步执行环境（IAsyncEnumerable\<object\>） |

- 所有节点基础类型中包含一个 Context 作为流程上下文

| Context 的属性 | 描述 |
| --- | --- |
| TriggerEvent | 触发导致进入当前节点的事件 |
| Data | 从之前节点传入的数据 |
| ManualLevel | 手动调试的级别 |
| Token | 当前流程的 Token，暂停或停止时变为取消状态，用于响应外部控制 |

- 节点使用特性

| 特性 | 使用范围 | 说明 |
| --- | --- | --- |
| FSMNodeAttribute | 类定义上 | 定义节点在脚本中的名称，可设定事件、显示信息及排序号 |
| FSMPropertyAttribute | 属性定义上 | 定义节点需要额外赋值的属性 |

#### 5. Yield 类

用于 EnumFSMNode 和 AsyncEnumFSMNode 中，插入 `yield return xxx;` 以增加暂停检查点。

| 类 | 描述 |
| --- | --- |
| IYieldAction | 基础接口，自定义 yield 操作 |
| Yield | 静态类：None（仅检查暂停）、Pause（自动暂停）、Retry（重新开始）、PauseRetry（暂停后重新开始） |
| YieldPriority | 按优先级暂停：`yield return (YieldPriority)4;` 当 ManualLevel > 4 时暂停 |
| YieldDelay | 延时：`yield return (YieldDelay)TimeSpan.FromSeconds(5);` 延时并检查暂停 |

#### 6. 节点常用类

| 类 | 描述 |
| --- | --- |
| StartNode | 启动节点，默认抛出 NextEvent |
| EndNode | 结束节点，默认抛出 EndEvent |
| IdleNode | 空闲节点，默认抛出 NextEvent |
| AccumulateNode | 累积计数节点，用于实现计次的 for 循环 |

**破坏性移除：运行时组合（#17）**

`GroupNode` / `ParallelNode` 的非泛型、单泛型、双泛型类型以及专属描述、子执行器、钩子、控制联动和包装出口已删除，旧 C# 源码需迁移，不能继续编译。旧类型键（含 `GroupT`/`GroupTU`/`ParallelT`/`ParallelTU`）、`[StartNode->EndEvent];`（包括 Module 模板内）在装配/导入阶段明确报错。API/WASM 拒绝旧创建与 `updateGroupDefs` 请求；React 不再提供组合编辑入口。

纯定义封装使用下文普通 Module 的 Input/Output 和真实 Branch；这不是等价迁移：父子生命周期、观察通知、暂停传播、Next/Cancel/Error 包装出口不会自动转换。Parallel 没有内置替代、ParallelModule 或隐含汇合，也不会静默串行化；独立并发由宿主组织不同 FSMEngine。新业务绑定、生命周期隔离、共享事件和协调启动属后续工单，本票不改变这些契约。

#### 7. 自定义 DSL 规则

##### 基本语法

```csharp
// 定义事件
def EndEvent event;

// 定义状态（括号内为 IFSMNodeFactory 构造时使用的名称）
def Start(Start)
{
    1 -> firstEvent;
    2 -> secondEvent;
}

def Step01(Demo01)
{
    1 -> firstEvent;
    2 -> secondEvent;
}

// 定义连线：(事件名) -> (原状态) to (目标状态)
firstEvent -> Start to Step01;
secondEvent -> Start to Step02;
```

##### 节点属性

节点定义中支持额外的属性分支：

```csharp
def MyNode(MyNodeType)
{
    1 -> NextEvent;
    Pos:(100, 200);          // 节点位置
    Color: "light-blue";     // 显示颜色
    Type: "special";         // 节点类型标签
    FlowID: "abc-123";       // 流程标识
}
```

##### 模块化系统

模块通过公开 Input（入口节点）和 Output（出口事件）复用流程。以下模块与主流程在同一文件中，**不需要 `import`**；括号中的节点类型 Key 需由节点工厂提供：

```csharp
module Checker {
    input Entry;
    output OK, NG;

    def Entry(Idle)
    {
        1 -> InternalDone;
        3 -> NG;
        Pos:(100, 100);
    }

    def Exit(End)
    {
        1 -> OK;
        Pos:(300, 100);
    }

    def InternalDone as event;
    def OK as event;
    def NG as event;

    InternalDone -> Entry to Exit;
}

// 普通主流程节点和事件
def Launch as event;
def Start(Start) { 1 -> Launch; }
def Target(Idle) {}
def Failure(End) {}

// 显式使用模块，不创建运行时模块节点
use Checker as CheckA;

// 普通事件转移：单入口模块可省略入口名
Launch -> Start to CheckA;

// 限定出口事件直接连接目标，无需输出别名映射
CheckA.OK -> Target;
CheckA.NG -> Failure;
```

**接口与连线规则：**

- `input` 声明公开的普通节点；单入口可写 `CheckA`，也可显式写 `CheckA.Entry`。多入口必须用 `实例名.入口名` 选择公开 Input，不能连接未公开的内部节点。
- `output` 声明公开事件，不是出口节点或终止节点。`CheckA.OK -> Target;` 也可连接另一个单入口实例（如 `CheckA.OK -> CheckB;`）；多入口则必须显式选择（如 `CheckA.OK -> CheckB.Retry;`，其中 `Retry` 必须是公开 Input）。
- 普通事件转移 `事件 -> 源节点 to 目标;` 保持不变，解析时仍优先按普通事件处理。普通点号事件名不会因此变成普通节点的成员访问；只有模块接口连接按公开 Input/Output 规则解析。
- 出口发布者由模块内部真实 Branch 的事件绑定确定：示例中 `Exit` 发布 `OK`，`Entry` 发布 `NG`。多个节点发布同一出口时全部连接；同一节点的多个 Branch 发布同一出口时按节点去重。外部连线不改写 Branch，也不靠重命名事件桥接流程。
- 不同实例保留独立的限定事件（如 `CheckA.OK`），不额外发布裸名全局出口 `OK`。`use` 的可选 body 仅允许 `Pos`、`Color`、`FlowID` 元数据，例如 `use Checker as CheckA { Pos:(200, 100); Color:"red"; FlowID:check; }`（替换上述无 body 的声明）。
- 模块是编译期复用单元：真实节点和事件以 `实例名.原名` 展平到父 `FSMEngine`。不创建运行时模块节点或独立引擎；视觉代理不参与执行，模块也不等同于 `GroupNode`。
- Namespace 仍独立用于以统一前缀组织节点和事件名称；它不能替代模块的公开接口和流程复用，模块也不替代 Namespace。两者都不提供运行时隔离。

**外部文件与源生成集成：**

若将上述 `module Checker { ... }` 定义移到外部 `Checker.fsm`，主脚本通过 `import` 引入，再使用模块（主流程节点及连线保持不变）：

```csharp
import Checker;
use Checker as CheckA;
```

文件系统查找的相对路径基于当前脚本文件目录。现有源生成器也支持将主脚本 `.FSMScript` 和模块 `.fsm` 作为 `AdditionalFiles`：

```xml
<ItemGroup>
  <AdditionalFiles Include="MainFlow.FSMScript" />
  <AdditionalFiles Include="Checker.fsm" />
</ItemGroup>
```

生成器按 `import` 从这些 `.fsm` 中收集模块文本，嵌入生成类的 `ModuleScripts`，在构建 `Engine` 时先注册模块再初始化主脚本；这条路径不要求运行时再读取模块文件。模块真实对象仍通过生成类已有的 `Engine` 按限定名查询，不新增模块专属强类型 API，也不把实例视觉代理当作 `IFSMNode` 访问：

```csharp
var flow = MainFlow.Create(factory);
var entry = flow.Engine.GetNode<StateMachine.IFSMNode>("CheckA.Entry");
var ok = flow.Engine.GetEvent("CheckA.OK");
```

**有意的破坏性变更：** 新模块 DSL 以 `use … as …` 和限定出口直连为唯一规范表达，不再支持 `terminal`、名称或数字输出映射、旧模块 `def` 实例声明，以及在外部事件转移中以实例名充当占位源节点。Input/Output 归模块定义所有，实例只选择并连接公开接口，不在实例 body 重定义出口。此变更不要求普通节点/事件脚本迁移，也不要求全面清理独立 C# Fluent API 或消费者依赖的旧公共字段。术语与设计依据见 [GLOSSARY](./GLOSSARY.md) 和 [ADR-0001](./docs/adr/0001-module-system-compile-time-expansion.md)。
