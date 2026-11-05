# Typed local Context (#18)

[简体中文](typed-context.md)

This is the new core C# entry point, not the final context-decoupling release. #21 pairs real DSL/Fluent/Module/generated consumers, all three factories and API/WASM hosts with this entry point. #19 owns execution occupancy, true exit and round isolation; #20 owns event sharing/coordinated start; #22 removes transitional entries.

## Nodes depend only on local business interfaces

```csharp
interface IQuery { string? Tasks { get; set; } }
interface IProcess { string? Tasks { get; } }
sealed class Job : IQuery, IProcess { public string? Tasks { get; set; } }

sealed class QueryNode : BaseFSMNode<IQuery>
{
    protected override Task ExecuteAsync()
    {
        Control.CheckPause();              // control, separate from business Context
        Context.Tasks = "queried";        // local interface, no Data wrapper
        PublishEvent(1, "query finished"); // payload is not business Context
        return Task.CompletedTask;
    }
}
sealed class ProcessNode : BaseFSMNode<IProcess>
{
    protected override Task ExecuteAsync()
    {
        Console.WriteLine(Context.Tasks);
        Console.WriteLine(TriggerEvent?.EventContext);
        PublishEvent(1);
        return Task.CompletedTask;
    }
}
```

`BaseFSMNode<T>`, `EnumFSMNode<T>` and `AsyncEnumFSMNode<T>` borrow business objects through getter-only `Context : T`. Nodes never know the aggregate type. Non-generic families explicitly declare no business dependency; no empty business interface is needed. Yield receives `FSMExecutionControl`, with neither business Data nor event payloads.

## Assemble relationships once; supply one aggregate per execution

```csharp
sealed class FlowInput
{
    public Job Job { get; init; } = new();
    public Job Other { get; init; } = new();

    public static void Configure(FSMContextBindings<FlowInput> bindings)
    {
        bindings.DefaultSource(x => x.Job); // covers supported requirement interfaces
        bindings.Node<IProcess>("Work.Process", x => x.Other);
    }
}

// Build the complete engine through ordinary C# node/event/transition APIs first.
// The factory's GetNodeType(typeKey) must agree with the actual type and dependency.
var flow = engine.BindContext<FlowInput>("Query", "Done", FlowInput.Configure);
var input = new FlowInput();
var prepared = flow.Prepare(input); // resolves/fixes all necessary references now; no node initialization/execution
using var executor = await prepared.StartAsync();
await executor.ExecutorTask;       // existing executor; #19 completes exit/occupancy guarantees
```

With one compatible direct property, omit Configure: `engine.BindContext<SingleInput>("Query", "Done")`. Alternatively leave the entry unconfigured and call `prepared.StartAsync("Local.Entry", "Done")`; local starts still validate the entire plan. Each execution supplies only its aggregate; configuration never captures an execution's business instance. Node exceptions use actual qualified engine names, not type names, and survive incoming transitions.

`FSMContextPreparation` currently represents **prepared fixed-reference data**, not an occupied lifecycle handle. Do not concurrently start/reuse an engine or Node instance. #19 will connect the same preparation path to occupancy/release guarantees. For sequential reuse, wait for the previous execution to exit, then explicitly call `flow.Prepare(input)`. Legacy `RestartAsync`/`Data` cannot start business-dependent nodes or restart a typed execution. Re-run BindContext after changing the engine's node set.

## Rules and failure stages

- Priority: qualified node source > explicit interface default `Default<IQuery>(x => x.Job)` (including `DefaultSource` shorthand) > unique compatible direct property. Defaults and node exceptions coexist. Same-priority duplicates, conflicting defaults, missing sources and ambiguity fail; registration order never wins.
- Automatic scanning uses only the **declared types** of direct public-readable, instance, non-indexed properties. No field/collection/nested-object scanning, name/order matching or runtime-subtype guessing.
- Explicit expressions may only follow existing public property paths rooted in the input parameter, such as `x => x.Nested.Job`. No conversions, conditionals, calls, captured external objects, construction/copy/projection or indexers. Sources must have reference declaration types, preventing boxed business copies.
- Ordinary `public set` anywhere on a source path fails assembly. `init`, `private set` and getter-only properties are supported. The C# compiler rejects subsequent external assignment; the framework does not introduce a source generator validating arbitrary aggregate declarations.
- `BindContext` validates the **entire engine**, including unreachable nodes, and checks factory-declared/actual node types and requirement metadata. Failed assembly produces no executable plan. Future hosts must reuse this validator rather than implement another binding algorithm.
- `Prepare` resolves every necessary source before any node runs. Null aggregates/sources and getter failures fail without candidate fallback. Dynamic hosts use the separately named `PrepareDynamic(object?)`; wrong types also fail before nodes run. Normal C# callers use typed `Prepare(TInput)`.
- Diagnostics include known qualified node identities, requirements, paths, declared/actual types and root causes. Unknown DSL locations are not invented; assembly does not guess runtime types before instances exist.
- Source identity uses the complete property declaration chain, not the diagnostic name path; hidden same-name inherited properties can be independent sources. Multiple interfaces of one source share one fixed object per preparation. Later internal property replacement does not rebind this execution; business members (including collection references) remain mutable. Business-field readiness belongs to business nodes.

## Control, triggers and ownership

Use `Control.Token` / `Control.CheckPause()` for cancellation/pause and `TriggerEvent` for incoming business events. Transitions update the target trigger. Pause/continue preserves it; internal continue signals are not business events. A fresh execution clears every node's trigger before contract-based initialization. Persist payloads only by explicitly writing to business Context.

Loops and pause/continue do not clear business objects. The framework does not copy, roll back or Dispose caller-owned resources. Callers hold/release them; nodes clean up their own temporary resources. Initialize private per-execution state explicitly through contracts such as `InitBeforeStart`; no reflective field clearing. Cross-execution accumulation must be explicitly caller-owned.

## Transitional entries and evidence

`IFSMNode.Context`, the non-generic base's legacy `Context`, `FSMNodeContext[<T>]` and old `FSMExecutor` constructors/RestartAsync remain temporarily so existing no-business-dependency hosts, demos and generated consumers build. The new path never reads legacy Data. These are not final compatibility promises; old samples are not the typed business Context API.

Real execution/assembly tests: `tests/StateMachineTest/TypedContextFacts.cs`, `TypedExecutionFacts.cs`. Real compiler positive/negative cases: `bash tools/test-typed-context.sh`. Red/green evidence and scope: [#18 historical acceptance record](https://github.com/Naoki326/AnotherFSM/blob/0d80339f91b1063649d5880980bd04374ec1d097/.scratch/ticket-18/implementation.md). Host .NET regressions are not real Release WASM acceptance.
