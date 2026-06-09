<h1 align="center">AnotherFSM</h1>

<p align="center">A tool for rapidly building workflows based on finite state machines</p>

### Wiki
[![zread](https://img.shields.io/badge/Ask_Zread-_.svg?style=flat&color=00b0aa&labelColor=000000&logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB3aWR0aD0iMTYiIGhlaWdodD0iMTYiIHZpZXdCb3g9IjAgMCAxNiAxNiIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHBhdGggZD0iTTQuOTYxNTYgMS42MDAxSDIuMjQxNTZDMS44ODgxIDEuNjAwMSAxLjYwMTU2IDEuODg2NjQgMS42MDE1NiAyLjI0MDFWNC45NjAxQzEuNjAxNTYgNS4zMTM1NiAxLjg4ODEgNS42MDAxIDIuMjQxNTYgNS42MDAxSDQuOTYxNTYNS4zMTUwMiA1LjYwMDEgNS42MDE1NiA1LjMxMzU2IDUuNjAxNTYgNC45NjAxVjIuMjQwMUM1LjYwMTU2IDEuODg2NjQgNS4zMTUwMiAxLjYwMDEgNC45NjE1NiAxLjYwMDFaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00Ljk2MTU2IDEwLjM5OTlIMi4yNDE1NkMxLjg4ODEgMTAuMzk5OSAxLjYwMTU2IDEwLjY4NjQgMS42MDE1NiAxMS4wMzk5VjEzLjc1OTlDMS42MDE1NiAxNC4xMTM0IDEuODg4MSAxNC4zOTk5IDIuMjQxNTYgMTQuMzk5OUg0Ljk2MTU2QzUuMzE1MDIgMTQuMzk5OSA1LjYwMTU2IDE0LjExMzQgNS42MDE1NiAxMy43NTk5VjExLjAzOTlDNS42MDE1NiAxMC42ODY0IDUuMzE1MDIgMTAuMzk5OSA0Ljk2MTU2IDEwLjM5OTlaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik0xMy43NTg0IDEuNjAwMUgxMS4wMzg0QzEwLjY4NSAxLjYwMDEgMTAuMzk4NCAxLjg4NjY0IDEwLjM5ODQgMi4yNDAxVjQuOTYwMUMxMC4zOTg0IDUuMzEzNTYgMTAuNjg1IDUuNjAwMSAxMS4wMzg0IDUuNjAwMUgxMy43NTg0QzE0LjExMTkgNS42MDAxIDE0LjM5ODQgNS4zMTM1NiAxNC4zOTg0IDQuOTYwMVYyLjI0MDFDMTQuMzk4NCAxLjg4NjY0IDE0LjExMTkgMS42MDAxIDEzLjc1ODQgMS42MDAxWiIgZmlsbD0iI2ZmZiIvPgo8cGF0aCBkPSJNNCAxMkwxMiA0TDQgMTJaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00IDEyTDEyIDQiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIxLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPgo8L3N2Zz4K&logoColor=ffffff)](https://zread.ai/Naoki326/AnotherFSM)

[简体中文](./README.md) | English

### Demo

[Control, modify, and execute the demo process](https://naoki326.github.io/AnotherFSM)

### Introduction

AnotherFSM is a **tool library for quickly constructing processes based on finite state machines**. Unlike common workflow engines, its workflow part is solely based on finite state machines, which only define nodes and events without any other special structures.

The difference from common state machines is: usually, the nodes in a finite state machine only represent states, with actions as a separate concept to execute corresponding operations. In this tool, when jumping to a certain state node, the execution code of the class corresponding to the node is automatically executed. That is, it combines actions and states into a simplified state machine.

Additionally, a state machine DSL has been introduced to quickly build state machine flow diagrams. The DSL design is inspired by Martin Fowler's work, and the implementation is based on Antlr4.

### Project Structure

| Project | Description |
| --- | --- |
| **StateMachine** | Core library, based on .NET Standard 2.0, fully functional and usable independently |
| **FSMDemo.API** | Web visual editor backend, ASP.NET Core Web API + SignalR |
| **FSMDemo.Frontend** | Web visual editor frontend, React + xyflow + Ant Design |
| **FSMDemo.Wasm** | Browser WASM demo, based on Blazor WebAssembly, runs state machines in-browser without a backend |
| **DemoNodes** | Demo custom node collection |
| **FSMScriptAnalyzer** / **FSMNodeAnalyzer** | Antlr4-based script/node analyzers |
| **FSMScriptAnalyzerTest** / **FSMNodeAnalyzerTest** | Analyzer test projects |

### Dependencies

- **StateMachine** is based on **.NET Standard 2.0**, can be used independently without UI dependencies
- **FSMDemo.API** is based on **.NET 10**, providing RESTful API and SignalR real-time communication
- **FSMDemo.Frontend** is built with React + [xyflow](https://github.com/xyflow/xyflow) + [Ant Design](https://ant.design/), using [dagre](https://github.com/dagrejs/dagre) for auto-layout and [zustand](https://github.com/pmndrs/zustand) for state management, providing a flowchart visual editor with execution control
- **FSMDemo.Wasm** is based on **Blazor WebAssembly**, runs the state machine demo directly in the browser (no backend required)

### References

1. The script language processing part utilizes Antlr4[^antlr4] to generate syntax parsing code.

[^antlr4]: Antlr4 is a grammar parsing generation tool. Project link: [Antlr4](https://github.com/antlr/antlr4)

2. The frontend flow editor is built with [xyflow](https://github.com/xyflow/xyflow)[^xyflow] (React Flow).

[^xyflow]: xyflow is an open-source React flowchart/node editor library. Project link: [xyflow](https://github.com/xyflow/xyflow)

### Quick Start

Use [just](https://github.com/casey/just) to run:

```bash
# Start both API backend and frontend dev server
just dev

# Or start separately
just api        # Start API server (port 5079)
just frontend   # Start frontend dev server (port 5174)

# Browser WASM demo (no backend required)
just wasm       # Start WASM demo (port 5180)
just build-wasm # Publish WASM demo (for GitHub Pages)
```

### Simple Usage Tutorial (StateMachine project only)

Here's a simple project: [Getting Started](https://github.com/Naoki326/AnoterFSM.Demo)

The state machine designed by this project is based on two crucial classes: FSMEngine and FSMExecutor.

- ***FSMEngine***

This type is responsible for holding an overall state diagram structure. One FSMEngine object includes several nodes, events, and the relationships connecting nodes through events. FSMEngine contains a series of APIs for creating, building, and changing the state machine's graph structure. Additionally, it includes an API for building the graph structure by passing in scripts — a custom DSL language designed to describe state machines.

- ***FSMExecutor***

Each FSMExecutor instance manages an object that executes the state machine, corresponding to an execution thread of a state machine, capable of controlling and monitoring its execution.

- ***Custom Nodes***

When the state machine executes and enters a node, it calls the corresponding methods within the node object. Only after these methods complete execution can it jump to the next node. Custom node classes need to inherit a series of node base classes provided by this project and follow specific rules when writing code.

#### 1. IFSMNodeFactory

This interface is a node factory class called when FSMEngine constructs node objects. Users need to implement it themselves, passing in the node type's Key value when constructing nodes.

##### Reflection-based Node Factory

The Demo project provides a `ReflectionNodeFactory` implementation that works without an IoC container:

```csharp
var factory = new ReflectionNodeFactory(Assembly.GetExecutingAssembly());
var engine = new FSMEngine(factory);
```

##### Autofac-based IoC Configuration

First, implement the IFSMNodeFactory interface:

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

Then register an Autofac Module in the project containing your custom nodes:

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

#### 2. State Machine Structure Management

- ***FSMEngine***

| Common Functions | Description |
| --- | --- |
| CreateNode | Creates a node, requires node type and name |
| ReinitGroupNode | Initializes GroupNode or ParallelNode (call after CreateNode) |
| ConnectNode | Creates connections between nodes (event name, from node, to node) |
| ChangeNodeName | Modifies the node name |
| ChangeTransitionName | Modifies the connection name |
| DeleteTransition | Deletes a connection between nodes |
| ClearTransition | Deletes all connections of the current node |
| AttachEvent | Adds issuable events to the current node |

- Script-based construction:

| Common Functions | Description |
| --- | --- |
| CreateStateMachine | Creates an FSMEngine using a script |
| CreateStateMachineByFile | Creates an FSMEngine using a script file |
| Transform | Restructures the current state graph |
| TransformByFile | Restructures using a script file |
| ToString | Outputs the state graph as a script |

- ***FSMEngineBuilder*** (Fluent API)

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

#### 3. State Machine Execution

- ***FSMExecutor***

Every FSMExecutor instance manages a state machine execution object that can control and monitor execution.

> The state flow executes within a Task; if an exception occurs, the Task automatically exits. Check IObservable's OnError or NodeExceptionEvent for exceptions.

| Functions | Description |
| --- | --- |
| FSMExecutor | Constructor, requires a starting node and completion event |
| RestartAsync | Restarts (auto-calls stop, waits for previous execution to exit) |
| PauseAsync | Pauses (waits for current node to properly pause) |
| Continue | Continues from paused position (synchronous) |
| StopAsync | Stops (waits for blocking operations in current node) |

- Monitoring

| Interface | Type | Description |
| --- | --- | --- |
| State | Property | Current process execution state |
| FSMStateChanged | Event | Process execution state change event |
| IObservable | Interface | Subscribe to all node-related events |
| NodeStateChanged | Event | Node entry event |
| NodeExitChanged | Event | Node exit event |
| NodeExceptionEvent | Event | Node exception event |
| TrackStateEvent | Event | Passes all events from IObservable |
| TrackCallEvent | Event | Emitted when flow control methods are called |

#### 4. Node Base Classes

Custom node classes need to inherit from node base classes to execute corresponding code when the state machine jumps to the node.

| Class | Description |
| --- | --- |
| AbstractFSMNode | Initial abstract class; any type inheriting this can be used as a node |
| SimpleFSMNode | Most basic node type; only needs ExecuteMethodAsync implemented |
| EnumFSMNode | Node based on C# yield mechanism for local pause/continuation |
| AsyncEnumFSMNode | Like EnumFSMNode with async execution (IAsyncEnumerable) |

- All node base types include a Context as the flow context

| Context Property | Description |
| --- | --- |
| TriggerEvent | The event that triggered entry into the current node |
| Data | Data passed from the previous node |
| ManualLevel | Manual debugging level |
| Token | Current flow token; cancelled during pause/stop, used for responding to external control |

- Node attributes

| Attribute | Scope | Description |
| --- | --- | --- |
| FSMNodeAttribute | Class definition | Defines the node name in script, events, display info, sort order |
| FSMPropertyAttribute | Property definition | Defines additional properties on the node |

#### 5. Yield Classes

Used in EnumFSMNode and AsyncEnumFSMNode to insert pause checkpoints with `yield return xxx;`.

| Class | Description |
| --- | --- |
| IYieldAction | Base interface for custom yield operations |
| Yield | Static class: None (check pause only), Pause (auto-pause), Retry (restart), PauseRetry (pause then restart) |
| YieldPriority | Priority-based pause: `yield return (YieldPriority)4;` pauses when ManualLevel > 4 |
| YieldDelay | Delay: `yield return (YieldDelay)TimeSpan.FromSeconds(5);` delays and checks pause |

#### 6. Common Node Classes

| Class | Description |
| --- | --- |
| GroupNode | Process wrapper node, wraps another internal process |
| ParallelNode | Parallel process wrapper node, wraps multiple parallel processes |
| StartNode | Start node, emits NextEvent by default |
| EndNode | End node, emits EndEvent by default |
| IdleNode | Idle node, emits NextEvent by default |
| AccumulateNode | Accumulative counting node for counted for-loops |

#### 7. Custom DSL Rules

##### Basic Syntax

```csharp
// Define an event
def EndEvent event;

// Define a state (name in parentheses is the IFSMNodeFactory key)
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

// Define connections: (EventName) -> (FromState) to (ToState)
firstEvent -> Start to Step01;
secondEvent -> Start to Step02;
```

##### Node Properties

Node definitions support additional property branches:

```csharp
def MyNode(MyNodeType)
{
    1 -> NextEvent;
    Pos:(100, 200);          // Node position
    Color: "light-blue";     // Display color
    Type: "special";         // Node type label
    FlowID: "abc-123";       // Flow identifier
}
```

##### Module System

The DSL supports module definition and import for process reuse and composition:

```csharp
// Define a module
module Checker {
    input Entry;           // Declare input node name
    output OK, NG;         // Declare output event names
    terminal Entry, Exit;  // Declare terminal node names (exit points for module instance connections)

    def Entry(TestIdle)
    {
        1 -> InternalDone;
        3 -> NG;
        Pos:(100, 100);
    }

    def Exit(TestEnd)
    {
        1 -> OK;
        Pos:(300, 100);
    }

    def InternalDone as event;
    def OK as event;
    def NG as event;

    // Internal connections within the module
    InternalDone -> Entry to Exit;
}

// Import the module
import Checker;

// Instantiate the module (module name in parentheses)
def MyChecker(Checker) {
    OK -> CheckPassed;     // Map module output events to external events
    NG -> CheckFailed;
    Pos:(200, 100);
}

// Connect from a regular node to a module instance (auto-routes to input nodes)
Launch -> Start to MyChecker;

// Connect from module instance to other nodes (auto-resolves exit via terminal nodes)
CheckPassed -> MyChecker to NextStep;
```

**Three module declaration keywords:**

| Keyword | Description |
| --- | --- |
| `input` | Declares module input node names; external connections entering the module instance route to these internal nodes |
| `output` | Declares module output event names; mapped at instantiation via `eventName -> externalEvent;` |
| `terminal` | Declares module terminal node names; external connections from the module instance land on these internal nodes. With multiple terminal nodes, the exit is auto-matched by the events published by each node |

**Module connection resolution rules:**

- Connection into a module instance → auto-routes to `input`-declared internal nodes
- Connection from a module instance → matches exit via events published by `terminal` nodes; if only one terminal node, uses it directly; falls back to `output` mapping when no terminals exist
