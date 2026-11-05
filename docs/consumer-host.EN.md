# C# consumers and host configuration (#21)

[中文](consumer-host.md) · [Core contract](typed-context.EN.md) · [Evidence](implementation/21-consumer-host-migration.md)

## Explicit script/aggregate pairing

See `demos/BusinessContext.fsm` and `demos/DemoNodes/BusinessFlow.cs`. Query and Process depend only on `IQueryBusiness` and `IProcessBusiness`. One `DemoJob` implements both: Query writes 41, Process reads the same object and updates it to 42. Business uses `Context`, cancellation/pause uses `Control`, and event information uses `TriggerEvent`.

```csharp
var engine = new FSMEngine(factory);
engine.CreateStateMachine(script); // Or direct C# / Fluent Build; real Module/use expansion
var flow = engine.BindContext<DemoNodes.DemoBusinessInput>(); // Explicit type, not filename inference
using var prepared = flow.Prepare(new DemoNodes.DemoBusinessInput()); // Explicit instance each run
using var executor = await prepared.StartAsync("Start", "EndEvent");
await executor.Execution.Completion; // True exit before another Prepare
```

For several compatible sources, place relations in the aggregate and explicitly pass its configuration method:

```csharp
public static void Configure(FSMContextBindings<OrderInput> b)
{
    b.DefaultSource(x => x.Job); // Or Default<IQueryBusiness>(x => x.Job)
    b.Node<IProcessBusiness>("Area.B.Process", x => x.Other);
}
// engine.BindContext<OrderInput>(OrderInput.Configure)
```

A unique compatible direct public property needs no configuration. Multiple candidates require an explicit default. Node overrides use actual expanded names and survive transitions. Even a local entry validates the entire engine and prepares every necessary source. Null never falls back. Source properties are getter-only, private-set or init, not ordinary public-set. Unknown source positions are not fabricated.

## One Runtime, two transports

Both `EngineManager` and `RuntimeSession` accept a C# `HostContext`:

```csharp
var pairing = HostContext.Create<OrderInput>(() => CreateInputForThisRun(), OrderInput.Configure);
var manager = new EngineManager(logger, factory, context: pairing);
// Or new RuntimeSession(factory, publishJson, context: pairing)
```

This is not a dependency container. It does not discover aggregate classes, infer filenames or inspect this run's subtypes. Each start reassembles the complete actual graph and borrows this run's input. API and Browser `Program.cs` explicitly install the same `DemoBusinessHost.Create()`, selecting `DemoBusinessInput` for their editable demo workspace. Editing the script cannot change the aggregate type. A dependent graph without a host pairing fails candidate assembly. Dependency-free nodes do not manufacture empty business objects.

The demo host creates a new Job per execution. Its optional Approval service is deliberately absent: a `BusinessApproval` node fails preparation before any node runs. `ExecutionStatus.businessResult/businessRun` is a fixed, safe demo projection, not a business-object payload. CLR types and mutable business instances never cross JSInterop. Frontend start/end parameters stay unchanged; there is no arbitrary JSON/CLR dependency configuration panel.

## Start is not replacement

- Ordinary start immediately rejects pending preparation, prepared ownership, running/paused execution, stopping cleanup and Finished-before-true-exit. It does not stop the old execution, change its bindings, or clear UI state/logs. Explicitly `await StopExecution()` before requesting the next input.
- Source providers and node initialization do not hold the host status lock. Conflicting starts do not queue. Stop and Import wait for the startup barrier and actual execution cleanup. Core ownership/binding remains authoritative; hosts do not duplicate its algorithm.
- Import first parses and validates the complete candidate binding plan, then retires the old execution and waits for true exit and graph disposal before returning. An invalid candidate preserves the old execution. Old notifications are detached. This permission never extends to ordinary start.
- Use `prepared.StartAsync(start, endEvents, beforeStart)` for observers installed before the first node. Multiple end events preserve top-level End-node behavior; End nodes inside Modules are not global terminators.

## Production factories and browser boundaries

- API: `AssemblyScanningNodeFactory`, real assembly discovery, without requiring Autofac.
- Browser: closed `BrowserNodeFactory` registration for Start/End/Idle/Sleep/Accumulate and the three Business nodes. No arbitrary CLR registration, scanning, device plugins or external module files. Inline/registered Modules work.
- Autofac: real `ResolveKeyed<IFSMNode>`, e.g. `RegisterType<BusinessQueryNode>().Keyed<IFSMNode>("BusinessQuery")`. Inaccurate delegate metadata cannot substitute for a business dependency declaration; BindContext rejects inconsistent declared/actual types and requirements.

All three use the same core whole-engine validator. Diagnostics retain actual qualified node identity, key, declared/actual type, requirement and root cause; instance failures include source paths. Public Module Input/Output, publisher deduplication, unchanged Branch bindings, fully qualified events, instance ownership and independent Namespace semantics remain intact.

Release WASM roots reflection metadata in AnotherFSM/DemoNodes/FSMDemo.Runtime while trimming platform libraries. Real Chrome verifies getters, local interfaces, DSL, DTOs and JSInterop. Initialization failure offers explicit retry, never API fallback. Browser verification runs WASM first with the API offline and asserts zero REST/SignalR business requests.

## Generated consumers and retained entries

`BusinessModules.FSMScript` is a real AdditionalFile. Its generated class supplies Engine and accessors only; the host explicitly calls `generated.Engine.BindContext<DemoBusinessInput>()`. No generator change or new business-binding generator is needed. `tools/test-typed-context.sh` includes a real CS1503 negative compilation for a generated consumer's wrong aggregate input.

Retained for #22: `IFSMNode.Context`, non-generic legacy Context, `FSMNodeContext`/generic wrappers and Data/As, `FSMExecutor` constructors/four Restart overloads, unscoped Handle/engine.PublishEvent (unsafe external delivery already rejected), and the old `ReinitGroupNode` name (still serves every injectable node, not runtime composition). Runtime and existing dependency-free generated flows retain the no-Data constructor/Restart driver. Business flows use BindContext/Prepare/Execution only. FakeNode's legacy Context property only satisfies the transitional interface; it does not propagate business data. Demo/generated nodes no longer read Context.Token/Data or use caller casts.

#20 sharing/coordinated startup is separate. This ticket adds no sharing UI, WPF/WebView2, AOT, Worker or multithreaded deployment requirement. Host .NET regression is never browser evidence; see the per-layer verification record.
