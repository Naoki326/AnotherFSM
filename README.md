<h1 align="center">AnotherFSM</h1>

<p align="center">A tool for quickly building processes based on finite state machines</p>

English| [简体中文](./README.zh-CN.md)

### Demo

[Control, modify, and execute the demo process](https://naoki326.github.io/AnotherFSM)

### Introduction

AnotherFSM is a **tool library for quickly building processes based on finite state machines**. Unlike common workflow engines, its workflow part is solely based on finite state machines. Aside from nodes and events, no other special structures are defined.

The difference between this tool and usual state machines is that, in typical finite state machines, nodes only represent states, and there's an additional concept of actions to execute corresponding operations. This tool automatically executes the code corresponding to the node class when transitioning to a state node, combining actions and states into a simplified state machine.

### Dependencies

- The part of this project unrelated to the interface, i.e., the **StateMachine** project, depends on the Antofac.Annotation project[^annotation], both written in **NetStandard2.0**.

[^annotation]: Project link: [Antofac.Annotation](https://github.com/yuzd/Autofac.Annotation)

- The interface-related part of this project, i.e., the **StateMachine.FlowComponent** project, is written in **Net8.0** using Blazor. It extends the functionality of the **StateMachine** project, facilitating quick process construction through the interface.

- The **StateMachine** project is fully functional and can be used independently, without relying on **StateMachine.FlowComponent**.

- Other projects in this repository are implementations of the [Demo](https://naoki326.github.io/AnotherFSM).

### References

1. Utilizes the dependency injection tool Autofac and [Antofac.Annotation](https://github.com/yuzd/Autofac.Annotation)[^annotation] (recompiled with .netstandard2.0).

2. The script language processing part uses Antlr4[^antlr4] to generate syntax parsing code.

[^antlr4]: ANTLR (ANother Tool for Language Recognition) is a powerful parser generator for reading, processing, executing, or translating structured text or binary files. Project link: [Antlr4](https://github.com/antlr/antlr4)

3. The interface part is written based on [Masa Blazor](https://github.com/masastack/MASA.Blazor)[^masablazor] and [Drawflow](https://github.com/jerosoler/Drawflow)[^drawflow].

[^masablazor]: Masa Blazor is a blazor UI component library based on Material Design. Project link: [Masa Blazor](https://github.com/masastack/MASA.Blazor)
[^drawflow]: Drawflow is a Simple flow library. Project link: [Drawflow](https://github.com/jerosoler/Drawflow)

### Simple Usage Tutorial (StateMachine project only)

#### 1. IoC Configuration

- If using IHostBuilder, refer to the Demo and configure IoC as follows:

```C#
Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
    {
        Assembly assembly = Assembly.Load("StateMachine");
        Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
        Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
        // The assemblies here need to cover all assemblies containing implemented nodes to enable script-based node construction
        Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
        // Register all modules
        containerBuilder.RegisterAssemblyModules(assemblies);
        // Register all types with the Component attribute, including custom process nodes
        containerBuilder.RegisterModule(new AutofacAnnotationModule(assemblies)
            .SetAutoRegisterInterface(true)
            .SetAutoRegisterParentClass(false)
            .SetIgnoreAutoRegisterAbstractClass(true));
        containerBuilder.RegisterBuildCallback(c =>
        {
            // Configure the default global IoC instance
            IoC.ContainerWrapper = new ContainerWrapper(c);
        });
    })
```

- Non-IHostBuilder configuration reference:

```C#
var containerBuilder = new ContainerBuilder();
Assembly assembly = Assembly.Load("StateMachine");
Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
// The assemblies here need to cover all assemblies containing implemented nodes to enable script-based node construction
Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
// Register all modules
containerBuilder.RegisterAssemblyModules(assemblies);
// Register all types with the Component attribute, including custom process nodes
containerBuilder.RegisterModule(new AutofacAnnotationModule(assemblies)
    .SetAutoRegisterInterface(true)
    .SetAutoRegisterParentClass(false)
    .SetIgnoreAutoRegisterAbstractClass(true));

containerBuilder.RegisterBuildCallback(c =>
{
    // Configure the default global IoC instance
    IoC.ContainerWrapper = new ContainerWrapper(c);
});
containerBuilder.Build();
```

- Both methods are references. If familiar with IoC configuration, feel free to configure as needed.

#### 2. Process Structure Management Class

- ***FSMEngine***

- This type is responsible for saving an overall state graph structure. An FSMEngine object includes several nodes, events, and the relationships connecting nodes via events.

| Common Functions | Description |
| --- | --- |
| CreateNode | Creates a node, requiring the node type and node name |
| ReinitGroupNode | Since GroupNode needs to reference the current FSMEngine, after creating GroupNode or ParallelNode using CreateNode, this method should be called in FSMEngine to initialize the GroupNode |
| ConnectNode | Creates connections between nodes, input event name, from-node name, and to-node name to indicate that the event transitions the from-node to the to-node |
| ChangeNodeName | Changes the node name |
| ChangeTransitionName | Changes the transition name |
| DeleteTransition | Deletes a node transition |
| ClearTransition | Deletes all transitions of the current node |
| AddEvent | Adds an event that the current node can emit |

- Additionally, a simple finite state machine script syntax implemented using Antlr is available, allowing quick construction of state graphs and filling FSMEngine using scripts. The corresponding script can be viewed using Export in the Demo.

| Common Functions | Description |
| --- | --- |
| CreateStateMachine | Creates FSMEngine using a script |
| CreateStateMachineByFile | Creates FSMEngine using a script file |
| Transform | Reorganizes the current state graph, keeping the properties of existing nodes unchanged while changing transitions and allowing the addition of new nodes and events |
| TransformByFile | Reorganizes the current state graph using a script file |
| ToString | Outputs the current state graph as a script |

#### 3. Process Execution Class

- ***FSMExecutor***

- Each FSMExecutor instance manages an object executing the state machine, capable of controlling and monitoring the state machine execution.

- IMPORTANT: The state flow will execute within a Task. If the process encounters an exception, the Task will automatically exit. For any exceptions that occur during the process, you can check the IObservable's OnError or the NodeExceptionEvent event.

| Function | Description |
| --- | --- |
| FSMExecutor | Constructor, requiring a start node and a completion event |
| RestartAsync | Restarts, note that control methods are asynchronous. Restarting will automatically stop the previous execution, awaiting its proper termination |
| PauseAsync | Pauses, requiring the current node to correctly pause. If the current node is at a blocking method, the pause method will block until the blocking method completes correctly |
| Continue | Continues, this method is synchronous and can be called to resume execution from the pause point without waiting |
| StopAsync | Stops, requiring the current node's blocking operations to terminate |

- Monitoring

| Interface | Type | Description |
| --- | --- | --- |
| State | Property | The current execution state of the process |
| FSMStateChanged | Event | Event triggered when the process execution state (State) changes |
| IObservable | Interface | Based on IObservable, it provides subscription to all node-related events, including the following events: node entry, start node entry, cancellation, pause, continue, error, extra event discarding, etc. Note: The state flow will execute within a Task. If the process encounters an exception, the Task will automatically exit. For any exceptions that occur during the process, you can check the IObservable's OnError or the NodeExceptionEvent event. |
| NodeStateChanged | Event | Node entry event |
| NodeExitChanged | Event | Node exit event |
| NodeExceptionEvent | Event | Node exception event |
| TrackStateEvent | Event | Transmits all events emitted by the IObservable interface as events |
| TrackCallEvent | Event | Event emitted when process control methods are called, including RestartAsync, PauseAsync, Continue, StopAsync |

- Others: Implements IEnumerable interface, returning all successor nodes connected from the start node (depth-first search).

#### 4. Node Base Class

- Custom execution code needs to inherit from the node base class to implement the code executed when transitioning to the node.

| Class | Description |
| --- | --- |
| AbstractFSMNode | Initial abstract class for nodes, defining basic methods. Any type inheriting this class can be used as a node in this framework |
| SimpleFSMNode | The most basic node type. Custom nodes inheriting this type only need to implement the ExecuteMethodAsync method for use in this framework, defining the action executed upon entering this node. The pause method will wait for this method to complete before pausing |
| EnumFSMNode | Implements nodes with local pause and continue using C#'s yield mechanism. Custom nodes inheriting this type need to implement the ExecuteEnumerable method, which returns IEnumerable\<object\>. The method can have yield return x; statements to insert pause checkpoints. The process execution object will pause at these checkpoints when PauseAsync is called and resume at the pause point when continued |
| AsyncEnumFSMNode | Similar to EnumFSMNode, but adds asynchronous execution, with ExecuteEnumerable using IAsyncEnumerable\<object\> |

- Additionally, all node base types include a Context as the process context.

| Context Property | Description |
| --- | --- |
| TriggerEvent | The event that triggered entry into the current node |
| Data | Data passed from the previous node, if any, can be ignored if not passed |
| ManualLevel | Manual debugging level |
| Token | The current process's token, which becomes a cancellation state when pausing or stopping, typically used for blocking operations (e.g., Web, IO operations) to respond to external pause or stop requests |
| EnumResult | Reserved, not used |

- Node attributes FSMNode FSMProperty

| Attribute | Scope | Description |
| --- | --- | --- |
| FSMNodeAttribute | Class definition  | Defines the node name in the script, can also set possible emitted events, interface display information, and interface usable node sort order |
| FSMPropertyAttribute | Property definition | Defines additional properties to be assigned to the node, used in conjunction with DynamicObjectEditor in the Demo for interface assignment operations |

#### 5. Common Node Classes

- Provides native implementations of common nodes, ready to use without custom implementation.

| Class | Description |
| --- | --- |
| StartNode | Start node, emits NextEvent by default |
| EndNode | End node, emits EndEvent by default |
| IdleNode | Idle node, emits NextEvent by default |
| GroupNode | Process wrapper node, wraps another process inside. Requires StartName and EndEvent properties to be set. Emits NextEvent upon completion, and CancelEvent if the internal process aborts |
| ParallelNode | Parallel process wrapper node, wraps multiple processes inside. Requires FSMs property to be set with multiple processes' StartNode names and EndEvent names. Executes in parallel, emits NextEvent upon completion, and CancelEvent if any internal process aborts |
| AccumulateNode | Accumulation count node for implementing counted for-loops, requires Count property to be set. Emits NextEvent upon completion, and BreakEvent when counting reaches Count |

- Note: All common nodes include a demo delay. For normal use, remove the delay code, e.g., AccumulateNode's execution method:
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
```