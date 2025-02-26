<h1 align="center">AnotherFSM</h1>

<p align="center">A tool for rapidly building workflows based on finite state machines</p>

[简体中文](./README.md) | English

### Demo

[Control, modify, and execute demo workflows](https://naoki326.github.io/AnotherFSM)

### Introduction

AnotherFSM is a **tool library for rapidly building workflows based on finite state machines**. Unlike common workflow engines, its workflow part is solely based on finite state machines, defining only nodes and events without any special structure.

The difference from typical state machines is that typical finite state machines have nodes representing only states, with actions being separate concepts to execute corresponding operations. This tool automatically executes the code of the corresponding class when transitioning to a state node, combining action and state into a simplified state machine.

Additionally, a state machine DSL has been introduced to quickly construct state machine flowcharts. The DSL design is inspired by Martin Fowler's work and implemented based on Antlr4.

### Dependencies

- **StateMachine** project, part not related to the interface, is developed based on **NetStandard2.0**.

- **StateMachine.FlowComponent** project, the interface-related part, is based on **Net8.0** using Blazor. It is an extension of the **StateMachine** project for conveniently building workflows through the interface.

- The **StateMachine** project is fully functional and can be used independently without relying on **StateMachine.FlowComponent**.

- Other projects in this repository are implementations of the [Demo](https://naoki326.github.io/AnotherFSM).

### References

1. The script language processing part uses the Antlr4[^antlr4] tool to generate syntax parsing code.

[^antlr4]: Antlr4 is a syntax parsing generation tool. Project link [Antlr4](https://github.com/antlr/antlr4).

2. The interface part is developed based on [Masa Blazor](https://github.com/masastack/MASA.Blazor)[^masablazor] and [Drawflow](https://github.com/jerosoler/Drawflow)[^drawflow].

[^masablazor]: Masa Blazor is an open-source Blazor front-end framework. Project link [Masa Blazor](https://github.com/masastack/MASA.Blazor).
[^drawflow]: Drawflow is an open-source JS flowchart control. Project link [Drawflow](https://github.com/jerosoler/Drawflow).

### Quick Start Guide (StateMachine Project Only)

The state machine in this project operates based on two key classes: FSMEngine and FSMExecute.

- ***FSMEngine***

  This type is responsible for maintaining an overall state graph structure. An FSMEngine object includes multiple nodes, events, and the relationships between nodes connected through events. FSMEngine contains a series of APIs for creating, building, and modifying the graph structure of the state machine. It also includes an API for constructing the graph structure through script input, using a custom DSL language designed in this project to describe state machines.

- ***FSMExecute***

  Each FSMExecutor instance manages an object that executes the state machine, corresponding to a thread executing the state machine. This object can control and monitor the execution of the state machine.

- ***Custom Nodes***

  When the state machine executes and enters a node, it calls the corresponding method within that node object and transitions to the next node after completing the method execution. These nodes are user-defined but must inherit from a series of node base classes provided by this project, written according to specific rules. By inheriting these types, you can gain access to the node execution context in the code environment.

#### 1. IFSMNodeFactory

This interface is a node factory class called by FSMEngine when constructing nodes, requiring user implementation. When calling the FSMEngine to construct nodes, a node type Key value is passed, used for creating the corresponding node object through the factory class. Users might construct objects through various methods such as reflection or containers; therefore, implementing the node factory class is left to the user. The demo provides a method for constructing nodes based on Autofac.

##### IoC Configuration Based on Autofac

First, add an AutofacModule in the project containing all your custom nodes, as shown below:

```csharp
/// <summary>
/// Demonstrates using Autofac to inject designed demo nodes.
/// Injects using the Key marked by the FSMNodeAttribute attribute as the container's Key.
/// Extra caution needed to inject GroupNode and ParallelNode from StateMachine into the container as well.
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

        // If there are multiple Modules, call the following two lines of code in only one of them
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

Extra caution needed to inject GroupNode and ParallelNode from StateMachine into the container, but only once. This means if you have multiple custom node projects each having such a module, you only need to register GroupNode and ParallelNode in one of the Modules.

Then, inject the Module in the startup project.

- If using IHostBuilder, you can refer to the Demo for IoC configuration as shown below:

```csharp
Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
    {
        Assembly assembly = Assembly.Load("StateMachine");
        Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
        Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
        // The assemblies here should cover all assemblies implementing nodes to allow script auto-construction of nodes.
        Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
        // Register all Modules
        containerBuilder.RegisterAssemblyModules(assemblies);

        containerBuilder.RegisterBuildCallback(c =>
        {
            // Configure the default global IoC instance
            IoC.ContainerWrapper = new ContainerWrapper(c);
        });
    })
```

- Reference method not using IHostBuilder:

```csharp
var containerBuilder = new ContainerBuilder();
Assembly assembly = Assembly.Load("StateMachine");
Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
// The assemblies here should cover all assemblies implementing nodes to allow script auto-construction of nodes.
Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
// Register all Modules
containerBuilder.RegisterAssemblyModules(assemblies);

containerBuilder.RegisterBuildCallback(c =>
{
    // Configure the default global IoC instance
    IoC.ContainerWrapper = new ContainerWrapper(c);
});
containerBuilder.Build();
```

- Both methods are for reference, and you can freely configure if you're familiar with IoC configurations. 

#### 2. Statem Machine Management Class

- ***FSMEngine***

- This type is responsible for preserving a complete state graph. An FSMEngine object includes multiple nodes, events, and the relationships between nodes connected through events.

| Common Functions | Description |
| --- | --- |
| CreateNode | Create a node by inputting node type and name |
| ReinitGroupNode | As GroupNode needs to reverse reference the current FSMEngine, if a GroupNode or ParallelNode is created via CreateNode, this method needs to be called in FSMEngine to initialize the GroupNode |
| ConnectNode | Create connections between nodes by inputting event name, output node name, and input node name, indicating that the event causes the node transition from output to input |
| ChangeNodeName | Change node name |
| ChangeTransitionName | Change connection name |
| DeleteTransition | Delete a node connection |
| ClearTransition | Delete all connections of the current node |
| AttachEvent | Add an event that the current node can emit |

- Additionally, a simple finite state machine scripting syntax implemented with Antlr can be used to quickly build state diagrams and populate FSMEngine. You can use Export in the demo for the corresponding script.

| Common Functions | Description |
| --- | --- |
| CreateStateMachine | Create FSMEngine via script |
| CreateStateMachineByFile | Create FSMEngine via script file |
| Transform | Reshape the current state diagram while keeping existing node attributes unchanged and only altering connections, allowing for new nodes and events |
| TransformByFile | Reshape the current state diagram via script file |
| ToString | Output the current state diagram as a script |

#### 3. Statem Machine Execution Class

- ***FSMExecutor***

- Each FSMExecutor instance manages an object that executes the state machine, capable of controlling and monitoring its execution.
  
- Emphasis: The state flow executes in a Task, which exits automatically if either process encounters exceptions. Any process exceptions can be checked using the OnError interface of IObservable or via NodeExceptionEvent.

| Functions | Description |
| --- | --- |
| FSMExecutor | Constructor needs a start node and an end event |
| RestartAsync | Restart the flow. Note that all control methods are asynchronous, and RestartAsync automatically calls StopAsync to cease previous execution, requiring correct exit before proceeding |
| PauseAsync | Pause. Pausing requires waiting for the proper pause of the current execution node. If the current node is running in a blocked method, the pause method will also block until the blocking method correctly completes |
| Continue | Continue, a synchronous method allowing execution continuation from the pause position without waiting |
| StopAsync | Stop. Stopping requires waiting for the blocking operations in the current node |

- Monitoring Related

| Interface | Type | Description |
| --- | --- | --- |
| State | Property | Current execution state of the flow |
| FSMStateChanged | Event | Flow execution state changes event, altering the State |
| IObservable | Interface | Provides event subscription for all node-related events including the enter and exit of nodes, entering and exiting of startup nodes, cancel, pause, resume, error, discard of redundant events, etc. Note: as the state flow executes in a Task, any exception in the flow causes the Task to exit, allowing for checking exceptions using either the IObservable interface's OnError or the NodeExceptionEvent. |
| NodeStateChanged | Event | Node entry event |
| NodeExitChanged | Event | Node exit event |
| NodeExceptionEvent | Event | Node exception-throwing event |
| TrackStateEvent | Event | Transmits all events sent by the IObservable interface as events |
| TrackCallEvent | Event | Emits the event when flow control methods are called, including RestartAsync, PauseAsync, Continue, StopAsync |

- Others: Implements IEnumerable interface returning all successor nodes connected to the start node (DFS)

#### 4. Node Base Class

- Custom node classes should inherit node base classes to code, allowing definition of corresponding node actions performed upon transitioning to the node.

| Class | Description |
| --- | --- |
| AbstractFSMNode | Initial abstract class of nodes defining basic methods. Any type inheriting this class can be used as a node in the framework |
| SimpleFSMNode | Fundamental node type. Custom nodes inheriting this class need to implement the ExecuteMethodAsync method to work within the framework, defining the action performed when the state machine enters this node. Upon calling the executor's Pause, execution waits for the completion of this method before pausing |
| EnumFSMNode | Nodes implementing local pause and continue mechanisms based on C#'s yield mechanism. Custom nodes inheriting this class should implement the ExecuteEnumerable method, returning IEnumerable<object>. This method allows insertion of yield return (IYieldAction); statements at any position to add a pause checkpoint, pausing if flow execution calls PauseAsync, and automatically resumes at pause point afterward |
| AsyncEnumFSMNode | Similar to EnumFSMNode with an asynchronous execution environment. ExecuteEnumerable uses IAsyncEnumerable<object> |

- Additionally, all node base types contain a Context as the execution context.

| Context Properties | Description |
| --- | --- |
| TriggerEvent | Event triggering entry into this node |
| Data | Data transferred from the previous node, if any |
| ManualLevel | Manual debugging level |
| Token | Current flow token. When paused or stopped, Token changes to cancel status, generally utilized for blocking operations (e.g., Web or IO operations) to respond to external pause or stop commands |
| EnumResult | Placeholder, unused |

- Node Use Attributes FSMNode, FSMProperty

| Attribute | Usage Scope | Description |
| --- | --- | --- |
| FSMNodeAttribute | Applied to class declarations | Defines the name of nodes in the script, optionally allowing setting possible emitted events, display information, and node sorting order for the interface |
| FSMPropertyAttribute | Applied to property declarations | Defines properties needing assignment on nodes, used in conjunction with DynamicObjectEditor in the demo for interface operations |

#### 5. Yield Class

- The Yield class is specialized for derived classes of EnumFSMNode and AsyncEnumFSMNode. After inserting yield return xxx; statements in their execution methods ExecuteEnumerable, it adds a pause checkpoint at that position and executes the corresponding operation.

| Class | Description |
| --- | --- |
| IYieldAction | Base interface for custom action definition during yield return (IYieldAction); specifying the operation and subsequent flow action. Implement the InvokeAsync method, executing at the current position, setting Result indicating the follow-up action after executing InvokeAsync with options including None, Pause, Retry, or PauseRetry. |
| Yield | A static class containing four static objects: Yield.None, Yield.Pause, Yield.Retry, and Yield.PauseRetry. Yield.None indicates doing nothing but checking for pause at yield return Yield.None;. Yield.Pause signifies that the flow auto-pauses at the current position; Yield.Retry signifies re-execution from the node start; Yield.RetryPause signifies auto-pausing at the current position, resuming from the node start upon continuation |
| YieldPriority | Node pause based on priority. Usage: yield return (YieldPriority)4; performs a comparison between Context.ManualLevel and 4, pausing if greater, or merely checking if less. Allows the use of numeric or enum variables |
| YieldDelay | Delay node. Usage: yield return (YieldDelay)TimeSpan.FromSeconds(5); indicates a 5-second delay at the current position, checking for pauses afterward. Also allows numeric input representing milliseconds |

#### 6. Common Node Classes

- Provides native implementations of common nodes, which can be observed in DemoNodes for reference. It's recommended to apply relevant code in actual projects.

| Class | Description |
| --- | --- |
| GroupNode | Flow packaging node capable of wrapping another flow, requiring StartName and EndEvent properties. Upon completion, emits the NextEvent, and emits CancelEvent upon internal flow termination |
| ParallelNode | Parallel flow packaging node capable of wrapping multiple flows, requiring FSMs property assignment for inputting multiple flow StartNode and EndEvent strings. Executes in parallel, emitting NextEvent on completion, emitting CancelEvent on internal flow termination |
| StartNode | Starter node, default emitting NextEvent |
| EndNode | Terminator node, default emitting EndEvent |
| IdleNode | Idle node, default emitting NextEvent |
| AccumulateNode | Cumulative counting node for realizing count-based for loops, with Count property assignment needed. Emits NextEvent upon completion, and BreakEvent if counting reaches Count |

- Note: Common nodes implemented for demonstration introduce intentional delays. For proper implementation, remove delay-related code, as exemplified in AccumulateNode's execution method:
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
// Define an event: def EventName event;
def EndEvent event;

// Define a state with: 
// def StateName (used when invoking IFSMNodeFactory for construction) {
//    (branchCount | none | success | failed | break | cancel) -> EventName;
//    ... (multiple branches)
// }
// Use curly braces to set results triggering other events
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
// (EventName) -> (SourceState) to (TargetState)
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
```