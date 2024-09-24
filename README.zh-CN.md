<h1 align="center">AnotherFSM</h1>

<p align="center">基于有限状态机快速构建流程的工具</p>

 [English](./README.md)| 简体中文

### Demo

[演示流程的控制、修改、执行](https://naoki326.github.io/AnotherFSM)

### 介绍

AnotherFSM 是一个**基于有限状态机、快速构建流程的工具库**，不同于常见的工作流引擎，它的工作流部分仅仅基于有限状态机，除了节点、事件，没有定义其他特别的结构

与通常的状态机不同的地方在于：通常有限状态机的节点仅仅表示状态(State)，另有一个动作(Action)的概念以执行相应操作。而本工具在跳转到某一状态节点上时会自动执行该节点对应类的执行代码，也就是将动作(Action)和状态(State)结合在一起，是简化的状态机

### 依赖项
 
- 本工程与界面无关的部分，即**StateMachine**项目，依赖Antofac.Annotation项目[^annotation]，两者基于**NetStandard2.0**编写

[^annotation]: 项目链接[Antofac.Annotation](https://github.com/yuzd/Autofac.Annotation)

- 本工程界面相关部分，即**StateMachine.FlowComponent**项目基于**Net8.0**采用blazor编写，是**StateMachine**项目功能的扩展，便于通过界面快速构建流程

- **StateMachine**项目的功能完整，可以独立使用，不依赖于**StateMachine.FlowComponent**

- 本工程中其他项目均为[Demo](https://naoki326.github.io/AnotherFSM)的实现

### 引用

1. 使用了依赖注入工具Autofac，和[Antofac.Annotation](https://github.com/yuzd/Autofac.Annotation)[^annotation](用.netstandard2.0重新编译)

2. 脚本语言处理部分使用了工具Antlr4[^antlr4]生成语法解析代码

[^antlr4]: Antlr4是一个语法解析生成工具 项目链接[Antlr4](https://github.com/antlr/antlr4)

3. 界面部分基于[Masa Blazor](https://github.com/masastack/MASA.Blazor)[^masablazor]和[Drawflow](https://github.com/jerosoler/Drawflow)[^drawflow]编写

[^masablazor]: Masa Blazor 是一个开源blazor前端框架 项目链接[Masa Blazor](https://github.com/masastack/MASA.Blazor)
[^drawflow]: Drawflow 是一个开源JS流程图控件 项目链接[Drawflow](https://github.com/jerosoler/Drawflow)

### 简单使用教程(仅StateMachine项目)

#### 1. IoC配置

- 若使用IHostBuilder，可以参考Demo，如下所示配置IoC：

```C#
Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
    {
        Assembly assembly = Assembly.Load("StateMachine");
        Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
        Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
        //这里的assemblies需要覆盖所有包含实现了节点的程序集，以实现脚本自动构建节点
        Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
        //注册所有的Module
        containerBuilder.RegisterAssemblyModules(assemblies);
        //注册所有包含Component特性的类型
        //包括自定义的流程节点
        containerBuilder.RegisterModule(new AutofacAnnotationModule(assemblies)
            .SetAutoRegisterInterface(true)
            .SetAutoRegisterParentClass(false)
            .SetIgnoreAutoRegisterAbstractClass(true));
        containerBuilder.RegisterBuildCallback(c =>
        {
            //配置默认的全局IoC实例
            IoC.ContainerWrapper = new ContainerWrapper(c);
        });
    })
```

- 不使用IHostBuilder的参考方式：

```C#
var containerBuilder = new ContainerBuilder();
Assembly assembly = Assembly.Load("StateMachine");
Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
//这里的assemblies需要覆盖所有包含实现了节点的程序集，以实现脚本自动构建节点
Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
//注册所有的Module
containerBuilder.RegisterAssemblyModules(assemblies);
//注册所有包含Component特性的类型
//包括自定义的流程节点
containerBuilder.RegisterModule(new AutofacAnnotationModule(assemblies)
    .SetAutoRegisterInterface(true)
    .SetAutoRegisterParentClass(false)
    .SetIgnoreAutoRegisterAbstractClass(true));

containerBuilder.RegisterBuildCallback(c =>
{
    //配置默认的全局IoC实例
    IoC.ContainerWrapper = new ContainerWrapper(c);
});
containerBuilder.Build();
```

- 两种方法均为参考，若熟悉IoC的配置方式，可自行灵活配置。

#### 2. 流程结构管理类

  - ***FSMEngine***

- 该类型负责保存一个整体状态图结构，一个FSMEngine对象内部包括若干节点、事件及节点与节点之间通过事件相连接的关系。

| 常用函数 | 描述 |
| --- | --- |
| CreateNode | 创建一个节点，需要输入节点类型，节点名称 |
| ReinitGroupNode | 由于GroupNode需要反过来引用当前FSMEngine，若通过CreateNode创建了GroupNode或ParallelNode之后，需要在FSMEngine中调用该方法对GroupNode初始化一下 |
| ConnectNode | 创建节点之间的连线，输入事件名称、出节点名、进入节点名，表示该事件使得出节点转化为入节点 |
| ChangeNodeName | 修改节点名称 |
| ChangeTransitionName | 修改连线名称 |
| DeleteTransition | 删除一条节点连线 |
| ClearTransition | 删除当前节点的所有连线 |
| AddEvent | 为当前节点添加可发出的事件 |

- 另外利用Antlr实现了一套简单的有限状态机脚本语法，可用脚本快速构建状态图，填充FSMEngine，可在Demo中用Export查看对应脚本。

| 常用函数 | 描述 |
| --- | --- |
| CreateStateMachine | 通过脚本创建FSMEngine |
| CreateStateMachineByFile | 通过脚本文件创建FSMEngine |
| Transform | 对当前状态图进行重组，已有节点内的各种属性不变，连线改变，可新增节点、事件 |
| TransformByFile | 通过脚本文件对当前状态图进行重组 |
| ToString | 将当前对应的状态图输出为脚本 |

#### 3. 流程执行类

  - ***FSMExecutor***

  - 每个FSMExecutor实例管理一个执行状态机的对象，该对象可以控制、监控状态机的执行。

| 函数 | 描述 |
| --- | --- |
| FSMExecutor | 构造函数，需要传入一个启动节点与一个完成事件 |
| RestartAsync | 重启动，注意控制方法均为异步方法，重启动会自动调用停止，以停止前次的执行，该过程需等待前次执行的正确退出 |
| PauseAsync | 暂停，暂停需要等待当前执行节点的正确暂停，若当前节点正运行到阻塞方法中，暂停方法亦会阻塞直到阻塞方法正确执行完毕 |
| Continue | 继续，该方法为同步方法，当流程暂停时可调用该方法从暂停位置继续执行，这一过程不需要等待即可执行 |
| StopAsync | 停止，停止需要等待当前节点中的阻塞操作 |

- 监控相关

| 接口 | 类型 | 描述 |
| --- | --- | --- |
| State | 属性 | 当前流程执行的状态 |
| FSMStateChanged | 事件 | 流程执行的状态即State的改变事件 |
| NodeStateChanged | 事件 | 节点进入事件 |
| NodeExitChanged | 事件 | 节点退出事件 |
| NodeExceptionEvent | 事件 | 节点抛出异常事件 |
| IObservable | 接口 | 基于IObservable，提供订阅所有的节点相关事件，包括节点的进入、启动节点进入、取消、暂停、继续、错误、多余事件抛弃等 |
| TrackStateEvent | 事件 | 以事件方式传递上面IObservable接口发出的所有事件 |
| TrackCallEvent | 事件 | 流程控制方法调用时发出该事件，包括：RestartAsync、PauseAsync、Continue、StopAsync |

- 其他：实现IEnumerable接口，返回从开始节点起，枚举与其相连的所有后继节点(深度优先搜索)

#### 4. 节点基础类

  - 自定义的执行代码需继承节点基础类来编写，可实现跳转到该节点时执行相对应的节点代码

| 类 | 描述 |
| --- | --- |
| AbstractFSMNode | 节点的初始抽象类，定义了基本的方法，继承该类的任意类型都可以作为节点使用在本框架 |
| SimpleFSMNode | 最基础的节点类型，继承该类型的自定义节点只要自行实现ExecuteMethodAsync方法即可用于本框架，该ExecuteMethodAsync方法定义了状态机进入本节点时执行的动作，调用执行器的暂停时，将会等待该方法执行完毕后才会暂停 |
| EnumFSMNode | 基于C#的yield机制实现了局部暂停继续的节点，继承该类型的自定义节点要自行实现ExecuteEnumerable方法，该方法返回IEnumerable\<object\> ，方法中任意位置可以插入yield return x;的语句，以便在当前位置增加一个暂停的检查点，当流程执行对象调用PauseAsync时，遇到检查点即暂停执行，继续时将会在暂停点自动恢复 |
| AsyncEnumFSMNode | 同EnumFSMNode，在其基础上增加了异步执行的环境，即ExecuteEnumerable使用了IAsyncEnumerable\<object\> |

- 另外所有节点基础类型中包含一个Context作为流程上下文

| Context的属性 | 描述 |
| --- | --- |
| TriggerEvent | 触发导致进入当前节点的事件 |
| Data | 从之前节点传入的数据，若未传入则可忽略 |
| ManualLevel | 手动调试的级别 |
| Token | 当前流程的Token，暂停或停止时Token变为取消状态，通常用于阻塞操作(如Web、IO操作)，以响应外部的暂停或停止 |
| EnumResult | 暂留，未使用 |

- 节点使用特性 FSMNode FSMProperty

| 特性 | 使用范围 | 说明 |
| --- | --- | --- |
| FSMNodeAttribute | 添加在类定义上  | 定义节点在脚本中的名称，另外可以设定该节点可能发出的事件，设定界面显示信息及界面可用节点的排序号 |
| FSMPropertyAttribute | 属性定义上 | 定义节点上需要额外赋值的属性，这里在Demo中与动态控件(DynamicObjectEditor)联合使用，实现界面赋值操作 |

#### 5. 节点常用类

  - 提供常见节点的原生实现，不需要自己实现即可使用

| 类 | 描述 |
| --- | --- |
| StartNode | 启动节点，默认抛出NextEvent事件 |
| EndNode | 结束节点，默认抛出EndEvent事件 |
| IdleNode | 空闲节点，默认抛出NextEvent事件 |
| GroupNode | 流程包装节点，可在内部包装另一个流程，需要对该类的StartName和EndEvent属性赋值。完成时抛出NextEvent事件，若内部流程中止，抛出CancelEvent事件 |
| ParallelNode | 并行流程包装节点，可在内部包装多个流程，需要对该类的FSMs属性赋值，可输入多个流程的StartNode名称和EndEvent名称。执行时按并行执行，完成时抛出NextEvent事件，若内部流程中止，抛出CancelEvent事件 |
| AccumulateNode | 累积计数节点，用于实现计次的for循环，需要对Count属性赋值。完成时抛出NextEvent事件，若计数到Count次数，则发出BreakEvent事件 |

- 注意，所有通用节点执行部分增加了演示用的延时，若需要正常使用，需删除延时部分代码，如AccumulateNode的执行方法：
```C#
protected override async IAsyncEnumerable<object> ExecuteEnumerable()
{
    yield return null;
    //try
    //{
    //    await Task.Delay(500, Context.Token);
    //}
    //catch (OperationCanceledException ex)
    //{ }
    //yield return null;
    if (i < Count)
    {
        i++;
        PublishEvent(FSMEnum.Next);
    }
    else
    {
        i = 0;
        PublishEvent(FSMEnum.Break);
    }
    yield break;
}
```