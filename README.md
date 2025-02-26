<h1 align="center">AnotherFSM</h1>

<p align="center">基于有限状态机快速构建流程的工具</p>

简体中文 | [English](./README.EN.md)

### Demo

[演示流程的控制、修改、执行](https://naoki326.github.io/AnotherFSM)

### 介绍

AnotherFSM 是一个**基于有限状态机、快速构建流程的工具库**，不同于常见的工作流引擎，它的工作流部分仅仅基于有限状态机，除了节点、事件，没有定义其他特别的结构

与通常的状态机不同的地方在于：通常有限状态机的节点仅仅表示状态(State)，另有一个动作(Action)的概念以执行相应操作。而本工具在跳转到某一状态节点上时会自动执行该节点对应类的执行代码，也就是将动作(Action)和状态(State)结合在一起，是简化的状态机

另外推出了一种状态机DSL，用于快速构建状态机流程图。DSL设计参考了Martin Fowler的著作，实现则基于Antlr4

### 依赖项
 
- 本工程与界面无关的部分，即**StateMachine**项目，基于**NetStandard2.0**编写

- 本工程界面相关部分，即**StateMachine.FlowComponent**项目基于**Net8.0**采用blazor编写，是**StateMachine**项目功能的扩展，便于通过界面快速构建流程

- **StateMachine**项目的功能完整，可以独立使用，不依赖于**StateMachine.FlowComponent**

- 本工程中其他项目均为[Demo](https://naoki326.github.io/AnotherFSM)的实现

### 引用

1. 脚本语言处理部分使用了工具Antlr4[^antlr4]生成语法解析代码

[^antlr4]: Antlr4是一个语法解析生成工具 项目链接[Antlr4](https://github.com/antlr/antlr4)

3. 界面部分基于[Masa Blazor](https://github.com/masastack/MASA.Blazor)[^masablazor]和[Drawflow](https://github.com/jerosoler/Drawflow)[^drawflow]编写

[^masablazor]: Masa Blazor 是一个开源blazor前端框架 项目链接[Masa Blazor](https://github.com/masastack/MASA.Blazor)
[^drawflow]: Drawflow 是一个开源JS流程图控件 项目链接[Drawflow](https://github.com/jerosoler/Drawflow)

### 简单使用教程(仅StateMachine项目)

本工程设计的状态机，其运行基于两个重要类：FSMEngine、 FSMExecute
  - ***FSMEngine***
  
  该类型负责保存一个整体状态图结构，一个FSMEngine对象内部包括若干节点、事件及节点与节点之间通过事件相连接的关系。FSMEngine包含创建、构建、改变状态机的图结构的一系列API，另外，还包含通过传入脚本的方式构建图结构的API（该脚本是本工程设计的一种描述状态机的自定义DSL语言）

  - ***FSMExecute***
  
  每个FSMExecutor实例管理一个执行状态机的对象，该对象对应一个状态机的执行线程，该对象可以控制、监控状态机的执行
  
  - ***自定义节点***
  
  当状态机执行时，进入到节点中，将会调用对应的节点对象内的相应方法，并在方法执行完成后才能跳入下一个节点。这些节点是使用者自定义的节点，但这些自定义节点类需要继承本工程提供的一系列的节点基类，并按照一定的规则来编写代码。同时，继承这些类型，可以在代码编写环境中获得节点执行的上下文环境

#### 1. IFSMNodeFactory

这个接口是 FSMEngine 构造节点对象时调用的节点工厂类，需要使用者自行实现。在调用FSMEngine构造节点的方法时，传入的是节点类型的Key值，此时会调用到该工厂类来创建对应的节点对象。考虑到使用者可能会采用不同的方式构造对象，如：反射创建、容器创建等方式，将节点工厂类的实现交给使用者自行定义。但Demo中提供了一种基于Autofac构造的节点工厂的方法

##### 基于Autofac的IoC配置

首先，实现接口IFSMNodeFactory，它将用于创建FSMEngine实例

```C#
    /// <summary>
    /// 当使用Autofac作为容器时，实现该接口
    /// 该接口将作为StateMachine的FSMEngine类型的节点构造工厂
    /// </summary>
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

        //返回autofac的IContainer中找出keyedservice的key等于name，类型为IFSMNode的实例的Type
        public Type GetNodeType(string name)
        {
            // 查找以 Keyed 的形式注册，键匹配 `name`，服务类型是 IFSMNode
            var registration = container.ComponentRegistry.Registrations
                .FirstOrDefault(r =>
                    r.Services.OfType<KeyedService>().Any(s =>
                        s.ServiceKey.Equals(name) && s.ServiceType == typeof(IFSMNode)));

            // 如果找到对应的注册，则获取其实现类型，并返回
            if (registration != null)
            {
                return registration.Activator.LimitType;
            }

            // 如果没有找到匹配的服务，可以抛出异常或返回null
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

其次，在您的所有自定义节点所在的项目中增加一个AutofacModule，如下所示：

```C#
    /// <summary>
    /// 演示，使用Autofac注入设计的Demo节点
    /// 注入时按照FSMNodeAttribute特性标记的Key作为容器的Key
    /// 需要额外注意在注入时将StateMachine中的GroupNode和ParalleNode也注入到容器中
    /// </summary>
    internal class _YourProjName_Module : Module
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

            // 若有多个Module，则只要在其中一个Module中调用下面两行代码
            RegisterKeyedNode<GroupNode>(builder);
            RegisterKeyedNode<ParallelNode>(builder);

            builder.RegisterType<AutofacNodeFactory>().As<IFSMNodeFactory>().SingleInstance();
            base.Load(builder);
        }

        private void RegisterKeyedNode<T>(ContainerBuilder builder) where T : IFSMNode
        {
            if (typeof(T).GetCustomAttribute(typeof(FSMNodeAttribute)) is FSMNodeAttribute attr)
            {
                builder.RegisterType<T>().Keyed<IFSMNode>(attr.Key);
            }
        }
    }
```

需要额外注意在注入时将StateMachine中的GroupNode和ParalleNode也注入到容器中，但只需要注入一次。也就是说，如果您有多个自定义节点的项目，每个项目都有这样的Module，但只需要在其中一个Module中注册GroupNode和ParallelNode。

最后，在启动项目中注入Module

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

该类型负责保存一个整体状态图结构，一个FSMEngine对象内部包括若干节点、事件及节点与节点之间通过事件相连接的关系。

| 常用函数 | 描述 |
| --- | --- |
| CreateNode | 创建一个节点，需要输入节点类型，节点名称 |
| ReinitGroupNode | 由于GroupNode需要反过来引用当前FSMEngine，若通过CreateNode创建了GroupNode或ParallelNode之后，需要在FSMEngine中调用该方法对GroupNode初始化一下 |
| ConnectNode | 创建节点之间的连线，输入事件名称、出节点名、进入节点名，表示该事件使得出节点转化为入节点 |
| ChangeNodeName | 修改节点名称 |
| ChangeTransitionName | 修改连线名称 |
| DeleteTransition | 删除一条节点连线 |
| ClearTransition | 删除当前节点的所有连线 |
| AttachEvent | 为当前节点添加可发出的事件 |

- 另外利用Antlr实现了一套简单的有限状态机脚本语法，可用脚本快速构建状态图，填充FSMEngine，可在Demo中用Export查看对应脚本。

| 常用函数 | 描述 |
| --- | --- |
| CreateStateMachine | 通过脚本创建FSMEngine |
| CreateStateMachineByFile | 通过脚本文件创建FSMEngine |
| Transform | 对当前状态图进行重组，已有节点内的各种属性不变，连线改变，可新增节点、事件 |
| TransformByFile | 通过脚本文件对当前状态图进行重组 |
| ToString | 将当前对应的状态图输出为脚本 |

  - ***FSMEngineBuilder***
   
增加了一个FluentAPI的构建方式，可以下面的方式来创建FSMEngine实例：
``` C#
var engine = FSMEngineBuilder.Create()
    .ConfigureNodeFactory(new AutofacNodeFactory())
    .ConfigureFSMDefine(build =>
    {
        build.AddNode<SleepNode>("Sleep")
            .AddConnection("NextEvent", "Start", "Sleep")
            .AddNode("Sleep2", "SleepNodeKey")
            ;
    })
    .Build();
```

#### 3. 流程执行类

  - ***FSMExecutor***

每个FSMExecutor实例管理一个执行状态机的对象，该对象可以控制、监控状态机的执行。
  
强调：状态流将在Task中执行，若流程出现异常，Task自动退出，对于流程出现的任何异常，可检查IObservable接口的OnError或者NodeExceptionEvent事件

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
| IObservable | 接口 | 基于IObservable，提供订阅所有的节点相关事件，包括下面所有的事件，如：节点的进入、启动节点进入、取消、暂停、继续、错误、多余事件抛弃等。注意：状态流将在Task中执行，若流程出现异常，Task自动退出，对于流程出现的任何异常，可检查IObservable接口的OnError或者NodeExceptionEvent事件 |
| NodeStateChanged | 事件 | 节点进入事件 |
| NodeExitChanged | 事件 | 节点退出事件 |
| NodeExceptionEvent | 事件 | 节点抛出异常事件 |
| TrackStateEvent | 事件 | 以事件方式传递上面IObservable接口发出的所有事件 |
| TrackCallEvent | 事件 | 流程控制方法调用时发出该事件，包括：RestartAsync、PauseAsync、Continue、StopAsync |

- 其他：实现IEnumerable接口，返回从开始节点起，枚举与其相连的所有后继节点(深度优先搜索)

#### 4. 节点基类

  - 自定义的节点类需要继承节点基类来编写，可实现跳转到该节点时执行相对应的节点代码

| 类 | 描述 |
| --- | --- |
| AbstractFSMNode | 节点的初始抽象类，定义了基本的方法，继承该类的任意类型都可以作为节点使用在本框架 |
| SimpleFSMNode | 最基础的节点类型，继承该类型的自定义节点只要自行实现ExecuteMethodAsync方法即可用于本框架，该ExecuteMethodAsync方法定义了状态机进入本节点时执行的动作，调用执行器的暂停时，将会等待该方法执行完毕后才会暂停 |
| EnumFSMNode | 基于C#的yield机制实现了局部暂停继续的节点，继承该类型的自定义节点要自行实现ExecuteEnumerable方法，该方法返回IEnumerable\<object\> ，方法中任意位置可以插入yield return (IYieldAction);的语句，以便在当前位置增加一个暂停的检查点，当流程执行对象调用PauseAsync时，遇到检查点即暂停执行，继续时将会在暂停点自动恢复 |
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

#### 5. Yield类
  - 这里的Yield类只用于EnumFSMNode和AsyncEnumFSMNode这两种基础节点的派生类中，在他们的执行方法ExecuteEnumerable中插入yield return xxx;的语句后，会在当前位置增加一个暂停的检查点，并且执行相应的操作

| 类 | 描述 |
| --- | --- |
| IYieldAction | 基础接口，继承该接口可自定义执行到yield return (IYieldAction);时的操作，和针对当前流程的操作。需要实现InvokeAsync方法，会在当前位置执行，然后设置Result，该Result表示执行完InvokeAsync方法后对流程的操作，包括 None\Pause\Retry\PauseRetry 四种情况。 |
| Yield | 一个静态类，包含 Yield.None\Yield.Pause\Yield.Retry\Yield.PauseRetry 四个静态对象。Yield.None表示什么都不做，在yield return Yield.None;处只进行暂停的检查；Yield.Pause表示运行到当前位置时流程自动暂停；Yield.Retry表示运行到当前位置时从当前节点的头部重新开始流程；Yield.RetryPause表示运行到当前位置时流程自动暂停，继续时将从当前节点的头部重新开始流程 |
| YieldPriority | 按优先级暂停节点，使用方式：yield return (YieldPriority)4; 表示运行到当前位置时用Context.ManualLevel与数字4进行对比，若大于4则自动暂停，否则只检查暂停；这里除了使用数字也可以使用枚举变量 |
| YieldDelay | 延时节点，使用方式：yield return (YieldDelay)TimeSpan.FromSeconds(5); 表示运行到当前位置时延时5秒，并检查暂停；这里除了使用TimeSpan也可以使用数字，若使用数字则代表延时的毫秒数 |


#### 6. 节点常用类

  - 提供常见节点的原生实现，可以在DemoNodes中看到其实现，建议可以在实际项目中使用其相应代码

| 类 | 描述 |
| --- | --- |
| GroupNode | 流程包装节点，可在内部包装另一个流程，需要对该类的StartName和EndEvent属性赋值。完成时抛出NextEvent事件，若内部流程中止，抛出CancelEvent事件 |
| ParallelNode | 并行流程包装节点，可在内部包装多个流程，需要对该类的FSMs属性赋值，可输入多个流程的StartNode名称和EndEvent名称。执行时按并行执行，完成时抛出NextEvent事件，若内部流程中止，抛出CancelEvent事件 |
| StartNode | 启动节点，默认抛出NextEvent事件 |
| EndNode | 结束节点，默认抛出EndEvent事件 |
| IdleNode | 空闲节点，默认抛出NextEvent事件 |
| AccumulateNode | 累积计数节点，用于实现计次的for循环，需要对Count属性赋值。完成时抛出NextEvent事件，若计数到Count次数，则发出BreakEvent事件 |

- 注意，所有通用节点执行部分增加了演示用的延时，若需要正常使用，需删除延时部分代码，如AccumulateNode的执行方法：
```C#
protected override async IAsyncEnumerable<object> ExecuteEnumerable()
{
    yield return Yield.None;
    //try
    //{
    //    await Task.Delay(500, Context.Token);
    //}
    //catch (OperationCanceledException ex)
    //{ }
    //yield return Yield.None;
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

#### 7.自定义的DSL规则

例子：

```C#
// 定义一个事件
// 语法 def 事件名 event;
def EndEvent event;

// 定义一个状态
// 语法：
// def 状态名 (程序里用IFSMNodeFactory构造时使用的名称)
// {
//    (分支数 | none | success | failed | break | cancel) -> 事件名;
//    ...（定义多个分支）
// }
//状态的结果如果会触发其它事件，用大括号里面的分支语句再来设定
def Start(Start)
{
	1 -> firstEvent;
	2 -> secondEvent;
}

def Step01(Demo01)
{
	1 -> firstEvent;
	2 -> secondEvent;
	3 -> thirdEvent;
}

def Step02(Demo02)
{
	1 -> firstEvent;
	2 -> secondEvent;
	3 -> thirdEvent;
}

// 定义状态之间的连接
// 语法 (事件名称) -> (原状态) to (目标状态)
firstEvent -> Start to Step01;
secondEvent -> Start to Step02;

def Step11(Demo11)
{
	1 -> firstEvent;
	2 -> secondEvent;
	3 -> thirdEvent;
}

def Step12(Demo12)
{
	1 -> firstEvent;
	2 -> secondEvent;
	3 -> thirdEvent;
}

def Step13(Demo13)
{
	1 -> firstEvent;
	2 -> secondEvent;
	3 -> thirdEvent;
}

def Step21(Demo21)
{
	1 -> firstEvent;
	2 -> secondEvent;
	3 -> thirdEvent;
}

def Step22(Demo22)
{
	1 -> firstEvent;
	2 -> secondEvent;
	3 -> thirdEvent;
}

def Step23(Demo23)
{
	1 -> End;
	2 -> End;
	3 -> End;
}

End -> Step23 to End;

def End(End)
{
	0->EndEvent;
}

firstEvent -> Step01 to Step11;
secondEvent -> Step01 to Step12;
thirdEvent -> Step01 to Step13;

firstEvent -> Step02 to Step21;
secondEvent -> Step02 to Step22;
thirdEvent -> Step02 to Step23;


firstEvent -> Step11 to Start;
secondEvent -> Step11 to Start;
thirdEvent -> Step11 to Start;

firstEvent -> Step12 to Start;
secondEvent -> Step12 to Start;
thirdEvent -> Step12 to Start;

firstEvent -> Step13 to Start;
secondEvent -> Step13 to Start;
thirdEvent -> Step13 to Start;


firstEvent -> Step21 to Start;
secondEvent -> Step21 to Start;
thirdEvent -> Step21 to Start;

firstEvent -> Step22 to Start;
secondEvent -> Step22 to Start;
thirdEvent -> Step22 to Start;
```