# Typed local Context and execution lifecycle (#18 / #19)

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
using var prepared = flow.Prepare(input); // atomically owns the engine and fixes required references; no node runs
using var executor = await prepared.StartAsync();
var execution = executor.Execution;       // a concrete round, never follows a restart
await execution.Completion;              // node return + framework cleanup/unsubscription; same as ExecutorTask
```

With one compatible direct property, omit Configure: `engine.BindContext<SingleInput>("Query", "Done")`. Alternatively leave the entry unconfigured and call `prepared.StartAsync("Local.Entry", "Done")`; local starts still validate the entire plan. Each execution supplies only its aggregate; configuration never captures an execution's business instance. Node exceptions use actual qualified engine names, not type names, and survive incoming transitions.

A successful `FSMContextPreparation` exclusively owns the engine. Conflicting preparation/start throws `FSMException` immediately, without evaluating the conflicting input's sources, rebinding or implicitly stopping the previous execution. Failed preparation releases its temporary reservation. Explicitly `Dispose()` an abandoned, unstarted preparation. A preparation starts only once; a released one cannot start again. After startup, disposing the preparation does not replace the exit barrier.

For sequential reuse, await the previous exit and explicitly call `flow.Prepare(nextInput)`. Reusing the same business object is allowed only by supplying it again; residual bindings are never implicit input. Legacy `RestartAsync`/`Data` cannot start business-dependent nodes or restart a typed execution. Re-run BindContext after changing the engine's node set.

## Concrete executions, isolation and true exit

```csharp
execution.PublishEvent("OrderReady", payload); // resolves the target execution's local event
await execution.PauseAsync();
execution.Continue();
execution.Stop();                             // request only; the node may still be running
await execution.Completion;                   // borrowed resources may now be released
// Alternatively await execution.StopAsync(); // request and await true exit
```

- `FSMExecution` is the stable round-specific handle: `PublishEvent`, `Pause/PauseAsync`, `Continue`, `Stop/StopAsync`, and `Completion`. Active operations on stale handles throw `FSMException`; internal late notifications are safely ignored. Do not substitute a mutable current-execution alias for a concrete handle.
- Node publications and pause requests automatically capture their round, without global broadcast. Independent engines really run concurrently; identical names, shared event objects and cloned EventIDs do not authorize cross-engine delivery. Legacy `engine.PublishEvent` is limited to that engine's node execution scope; external callers migrate to `execution.PublishEvent`. Core-node `IFSMNode.CreateNewAsync/RunAsync` also requires the owning executor's current invocation; it cannot directly bypass preparation ownership or reset a canceled token.
- Node types, Module definitions and explicit business objects may be reused; Node instances cannot belong to different engines. Ownership uses reference identity, not custom `Equals`, with node and existing/requested owner diagnostics. Business-object synchronization remains the business's responsibility.
- While paused, both ordinary and business-end events queue until Continue. Business data and the original trigger remain intact; arbitrary code-stack resumption is not promised. Stopping/exited executions reject external business events. Continue after stopping returns failure; stale round handles throw.
- `Paused`, `Finished`, and returning from `Stop()` or `Dispose()` do not prove exit. Completion awaits RunAsync return, node callbacks, framework iterator cleanup (including asynchronous finally blocks), and event unsubscription. FSMState remains unchanged; no Prepared state is added.
- Cancellation callback failures are retained immediately in `executor.StopException`; `Completion` / `StopAsync` report them only after actual exit. Non-cooperative nodes, unfinished cleanup and caller wait timeouts keep ownership. Never rebind or dispose borrowed resources merely because a wait timed out.
- All four retained restart overloads await the old exit before initializing control/triggers, binding explicit Data, and starting. No-Data overloads never inherit old Data. `FSMExecutor` is a transitional driver: reacquire its `Execution` after restart; saved `FSMExecution` handles never follow. Typed executions use explicit stop/wait followed by a fresh Prepare.
- Initialize private node state through `InitBeforeStart` each round. The framework does not reflectively clear fields, roll back business mutations, or dispose caller objects. Business tasks detached from node lifetime gain no automatic reclamation guarantee.

#20 implements shared sets/coordinated startup; #21 migrates host normal-start replacement policy and full typed bindings; #22 removes transitional entries. This section does not prematurely promise those capabilities.

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

Real execution/assembly tests: `tests/StateMachineTest/TypedContextFacts.cs`, `TypedExecutionFacts.cs`, `ExecutionLifecycleFacts.cs`; affected editor regressions are in `tests/FSMDemoWasmTest/RuntimeSessionFacts.cs`. Real compiler positive/negative cases: `bash tools/test-typed-context.sh`. Red/green evidence and scope: [#18 historical acceptance record](https://github.com/Naoki326/AnotherFSM/blob/0d80339f91b1063649d5880980bd04374ec1d097/.scratch/ticket-18/implementation.md). Host .NET regressions are not real Release WASM acceptance.
