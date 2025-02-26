<h1 align="center">AnotherFSM</h1>

<p align="center">A tool for rapidly building workflows based on finite state machines</p>

[简体中文](./README.md) | English

### Demo

[Control, modify, and execute the demo process](https://naoki326.github.io/AnotherFSM)

### Introduction

AnotherFSM is a **tool library for quickly constructing processes based on finite state machines**. Unlike common workflow engines, its workflow part is solely based on finite state machines, which only define nodes and events without any other special structures.

The difference from common state machines is: usually, the nodes in a finite state machine only represent states, with actions as a separate concept to execute corresponding operations. In this tool, when jumping to a certain state node, the execution code of the class corresponding to the node is automatically executed. That is, it combines actions and states into a simplified state machine.

Additionally, a state machine DSL has been introduced to quickly build state machine flow diagrams. The DSL design is inspired by Martin Fowler's work, and the implementation is based on Antlr4.

### Dependencies

- The main part of the project, i.e., the **StateMachine** project, is written based on **NetStandard2.0**.
  
- The UI-related part of this project, i.e., the **StateMachine.FlowComponent** project, is written using Blazor based on **Net8.0**. It serves as an extension to the **StateMachine** project, facilitating rapid process construction through interfaces.

- The **StateMachine** project has complete functionality and can be used independently, without depending on **StateMachine.FlowComponent**.

- Other projects in this engineering are implementations for [Demo](https://naoki326.github.io/AnotherFSM).

### References

1. The script language processing part utilizes Antlr4[^antlr4] to generate syntax parsing code.

[^antlr4]: Antlr4 is a grammar parsing generation tool. Project link: [Antlr4](https://github.com/antlr/antlr4)

3. The UI part is based on [Masa Blazor](https://github.com/masastack/MASA.Blazor)[^masablazor] and [Drawflow](https://github.com/jerosoler/Drawflow)[^drawflow].

[^masablazor]: Masa Blazor is an open-source Blazor front-end framework. Project link: [Masa Blazor](https://github.com/masastack/MASA.Blazor)
[^drawflow]: Drawflow is an open-source JavaScript flowchart control. Project link: [Drawflow](https://github.com/jerosoler/Drawflow)

### Simple Usage Tutorial (StateMachine project only)

The state machine designed by this project is based on two crucial classes: FSMEngine and FSMExecute.

  - ***FSMEngine***

  This type is responsible for holding an overall state diagram structure. One FSMEngine object includes several nodes, events, and the relationships connecting nodes through events. FSMEngine contains a series of APIs for creating, building, and changing the state machine's graph structure. Additionally, it includes an API for building the graph structure by passing in scripts—which is a custom DSL language designed to describe state machines.

  - ***FSMExecute***

  Each FSMExecutor instance manages an object that executes the state machine. This object corresponds to an execution thread of a state machine and can control and monitor the execution of the state machine.

  - ***Custom Nodes***

  When the state machine executes and enters the node, it will call the corresponding methods within the node object. Only after these methods complete execution can it jump to the next node. These nodes are user-defined, but such custom node classes need to inherit a series of node base classes provided by this project and write code according to specific rules. Furthermore, by inheriting these types, you can obtain the execution context environment of the node in the code writing environment.

#### 1. IFSMNodeFactory

This interface is a node factory class called when FSMEngine constructs node objects, and it requires the user to implement it themselves. When calling the FSMEngine method to construct a node, the key value of the node type is passed in, at which point this factory class will be called to create the corresponding node object. Considering that users might adopt different approaches to construct objects such as reflection creation, container creation, etc., the implementation of the node factory class is left to the user to define themselves. However, the Demo provides a method for constructing the node factory based on Autofac.

##### IoC Configuration Based on Autofac

Firstly, implement the IFSMNodeFactory interface, which will be used to create FSMEngine instances.

```csharp
    /// <summary>
    /// Implement this interface when using Autofac as a container.
    /// This interface serves as a node construction factory for the FSMEngine type in the StateMachine.
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

        // Return the Type of the instance found in the Autofac IContainer with keyedservice key equal to name and type IFSMNode
        public Type GetNodeType(string name)
        {
            // Look for a match to the registration registered as Keyed form where the key matches `name` and the service type is IFSMNode
            var registration = container.ComponentRegistry.Registrations
                .FirstOrDefault(r =>
                    r.Services.OfType<KeyedService>().Any(s =>
                        s.ServiceKey.Equals(name) && s.ServiceType == typeof(IFSMNode)));

            // If found, get its implementation type and return it
            if (registration != null)
            {
                return registration.Activator.LimitType;
            }

            // If no matching service is found, an exception can be thrown or null returned
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

Next, add an AutofacModule to your project containing all custom nodes as follows:

```csharp
    /// <summary>
    /// Demo, inject the demo node designed with Autofac
    /// When injected, the Key marked by the FSMNodeAttribute feature as the key of the container
    /// Note in particular the injection of the GroupNode and ParallelNode from StateMachine into the container
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

            // If there are multiple modules, just call the following two lines of code in one of the modules
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

It is particularly important to note that when injecting, inject GroupNode and ParallelNode from StateMachine into the container, but only once. In other words, if you have multiple projects with custom nodes, each project has such a module, but you only need to register GroupNode and ParallelNode in one of the modules.

Finally, inject the Module in the startup project.

- If using IHostBuilder, refer to the Demo, and configure IoC as shown below:

```csharp
Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
    {
        Assembly assembly = Assembly.Load("StateMachine");
        Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
        Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
        // The assemblies here need to cover all assemblies that implement nodes to achieve automatic script construction nodes.
        Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
        // Register all modules
        containerBuilder.RegisterAssemblyModules(assemblies);
       
        containerBuilder.RegisterBuildCallback(c =>
        {
            // Configure the default global IoC instance
            IoC.ContainerWrapper = new ContainerWrapper(c);
        });
    })
```

- Reference method without using IHostBuilder:

```csharp
var containerBuilder = new ContainerBuilder();
Assembly assembly = Assembly.Load("StateMachine");
Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
// The assemblies here need to cover all assemblies that implement nodes to achieve automatic script construction nodes.
Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
// Register all modules
containerBuilder.RegisterAssemblyModules(assemblies);

containerBuilder.RegisterBuildCallback(c =>
{
    // Configure the default global IoC instance
    IoC.ContainerWrapper = new ContainerWrapper(c);
});
containerBuilder.Build();
```

- Both approaches are for reference. If familiar with IoC configuration methods, you can flexibly configure them on your own.

#### 2. State Machine Structure Management Class

  - ***FSMEngine***

This type is responsible for holding an overall state diagram structure. An FSMEngine object includes various nodes, events, and the relationships linking nodes through events.

| Common Functions | Description |
| --- | --- |
| CreateNode | Creates a node, requires input of node type, node name. |
| ReinitGroupNode | Since GroupNode needs to reference the current FSMEngine, after creating GroupNode or ParallelNode via CreateNode, it needs to call this method in FSMEngine to initialize GroupNode. |
| ConnectNode | Creates connections between nodes by inputting event name, outgoing node name, and destination node name, indicating the event causes the outgoing node to transform into an incoming node. |
| ChangeNodeName | Modifies the node name. |
| ChangeTransitionName | Modifies the name of the connection line. |
| DeleteTransition | Deletes a connection between nodes. |
| ClearTransition | Deletes all connections of the current node. |
| AttachEvent | Adds issuable events to the current node. |

- Additionally, leveraging Antlr, a simple finite state machine script syntax is implemented, allowing rapid state graph construction through script, filling FSMEngine, and enabling script export in Demo for corresponding state graph observation.

| Common Functions | Description |
| --- | --- |
| CreateStateMachine | Creates an FSMEngine using the script. |
| CreateStateMachineByFile | Creates an FSMEngine using a script file. |
| Transform | Restructures the current state graph; various attributes within existing nodes remain unchanged, lines are modified, with potential addition of nodes and events. |
| TransformByFile | Restructures the current state graph using a script file. |
| ToString | Outputs the corresponding state graph as a script. |

  - ***FSMEngineBuilder***

A Fluent API construction method is added, allowing FSMEngine instance creation in the following way:

```csharp
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

#### 3. State Machine Execution Class

  - ***FSMExecutor***

Every FSMExecutor instance manages an object that executes a state machine, with the ability to control and monitor its execution.

Emphasis: The state flow will execute within a Task, and if an exception occurs in the process, the Task automatically exits. For any exception that arises in the process, one can check the IObservable interface's OnError or the NodeExceptionEvent event.

| Functions | Description |
| --- | --- |
| FSMExecutor | Constructor requiring a starting node and a completion event. |
| RestartAsync | Restarts, noting that control methods are asynchronous. Restart will automatically call Stop to halt the previous execution, and this process requires the proper exit of the previous execution. |
| PauseAsync | Pause, needing to wait for the correct pause of the currently executing node. If the current node is operating to a blocking method, the pause method will also be blocked until the blocking method is properly executed. |
| Continue | Continue, a synchronous method allowing resumption from the paused position when the flow is paused, executing without the need to wait. |
| StopAsync | Stop, requiring waiting for blocking operations in the current node. |

- Monitoring Related

| Interface | Type | Description |
| --- | --- | --- |
| State | Attribute | The state of the current process execution. |
| FSMStateChanged | Event | Event triggered by a change in the process execution status. |
| IObservable | Interface | Provides subscription to all node-related events based on the IObservable interface, including all events below such as node entry, start node entry, cancellation, pause, continuation, error, extra event discard, etc. Note: The state flow will execute within a Task, and if an exception occurs in the process, the Task automatically exits, for any exception that arises in the process, one can check the IObservable interface's OnError or the NodeExceptionEvent event. |
| NodeStateChanged | Event | Node entry event. |
| NodeExitChanged | Event | Node exit event. |
| NodeExceptionEvent | Event | Exception event raised by nodes. |
| TrackStateEvent | Event | Event passing all events issued by the IObservable interface above. |
| TrackCallEvent | Event | The event issued when calling the flow control methods, including: RestartAsync, PauseAsync, Continue, StopAsync. |

- Others: Implements IEnumerable interface to return all successor nodes connected to the start node through depth-first search.

#### 4. Node Base Classes

  - Custom node classes need inheritance from node base classes to write, enabling the execution of specific node code upon jumping to the node.

| Class | Description |
| --- | --- |
| AbstractFSMNode | The initial abstract class of nodes, defining basic methods. Any type inheriting this class can be used as a node within this framework. |
| SimpleFSMNode | The most basic node type. Custom node types inheriting this require implementation of the ExecuteMethodAsync method to be used in this framework. This method defines the action to perform upon the state machine entering this node, awaiting the completion of the method execution upon invoking the executor's pause. |
| EnumFSMNode | A node based on C#’s yield mechanism, enabling local pause and continuation. Custom nodes inheriting this need to implement the ExecuteEnumerable method, returning an IEnumerable<object>. The method can have yield return (IYieldAction); inserted anywhere to provide a pause checkpoint for the flow. Upon calling PauseAsync, the execution pauses at the checkpoint, resuming at the pause point when continued. |
| AsyncEnumFSMNode | Similar to EnumFSMNode, additionally providing an asynchronous execution environment—ExecuteEnumerable utilizes IAsyncEnumerable<object>. |

- Moreover, all basic node types include a Context as the flow's context.

| Context Property | Description |
| --- | --- |
| TriggerEvent | The event that triggered the entry into the current node. |
| Data | Data passed from the previous node, ignore if not passed. |
| ManualLevel | Manual debugging level. |
| Token | The current flow’s token, becomes cancelled during pause or stop. Usually used in blocking operations (e.g., web, IO operations) to respond to external pausing or stopping. |
| EnumResult | Placeholder, not in use |

- Nodes utilize attributes FSMNode FSMProperty

| Attribute | Usage Scope | Description |
| --- | --- | --- |
| FSMNodeAttribute | Added on class definition | Defines the name of the node in the script, additionally setting the node's possible issued events, interface display information, and sorting number of usable nodes in the interface |
| FSMPropertyAttribute | Attribute definition | Defines additional attributes needing assignment on nodes. Here, in collaboration with dynamic control (DynamicObjectEditor) in the Demo, achieving interface assignment operation |

#### 5. Yield Classes

  - Yield classes here are only for derived classes of the two basic node types: EnumFSMNode and AsyncEnumFSMNode. Upon inserting yield return xxx; statements in their execution methods ExecuteEnumerable, a pause checkpoint is created at that position alongside the corresponding operation.

| Class | Description |
| --- | --- |
| IYieldAction | Base interface, enabling custom operations to perform upon execution to yield return (IYieldAction);, and actions towards the current flow—requiring implementation of the InvokeAsync method, exercising at the current point then setting the Result. The Result indicates the operation towards the flow upon completing InvokeAsync, including None\Pause\Retry\PauseRetry four situations. |
| Yield | A static class comprising four static objects: Yield.None, Yield.Pause, Yield.Retry, Yield.PauseRetry. Yield.None implies doing nothing while only performing pause check at yield return Yield.None.; Yield.Pause means the flow auto-pauses upon arriving at this point; Yield.Retry means restarting the flow from the head of the current node upon execution to this point; Yield.PauseRetry means auto-pausing the flow upon execution to this point, resuming at the pause point upon continuation and restarting the flow from the head of the current node |
| YieldPriority | Pauses node by priority—usage: yield return (YieldPriority)4; implies auto-pausing upon a greater ManualLevel than 4—applies number or enum variable. |
| YieldDelay | Delay node—usage: yield return (YieldDelay)TimeSpan.FromSeconds(5); implies a 5-second delay upon execution to this point and pause check—applicable to TimeSpan or number representing millisecond delay |

#### 6. Common Node Classes

  - Provides native implementations of common nodes visible within DemoNodes, where corresponding code usage in practical projects is advised.

| Class | Description |
| --- | --- |
| GroupNode | Process wrapper node, packing an inner process requiring property StartName and EndEvent assignment, throwing NextEvent on completion and CancelEvent upon inner process termination. |
| ParallelNode | Parallel process wrapper node—packing multiple processes with property FSMs assignment capable of multiple processes' StartNode name and EndEvent name entry. Simultaneous execution emits NextEvent on completion and CancelEvent upon inner process termination. |
| StartNode | Start node, defaults to NextEvent emission. |
| EndNode | End node, emitting EndEvent by default. |
| IdleNode | Idle node, defaulting to NextEvent emission. |
| AccumulateNode | Accumulative counting node for implementing counted for-loop, needs Count assignment—NextEvent emission on completion while emitting BreakEvent upon count reaching Count instances. |

- Note: All common node execution portions incorporate demonstration delays—with normal use requiring delay code removal, as seen in AccumulateNode's execution method:

```csharp
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

#### 7. Custom DSL Rules

Example:

```csharp
// Define an event
// Syntax def EventName event;
def EndEvent event;

// Define a state
// Syntax:
// def StateName (Name used by IFSMNodeFactory construction in the program)
// {
//    (branchNumber | none | success | failed | break | cancel) -> EventName;
//    ... (define multiple branches)
// }
// State results that would trigger other events are defined through branch statements within braces.
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

// Define connections between states
// Syntax (EventName) -> (From State) to (To State)
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

--- 

This translation captures the essence of AnotherFSM, providing an understanding of its usage, framework structure, and script guidelines.