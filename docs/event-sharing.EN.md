# Business event sharing and coordinated startup

[中文](event-sharing.md)

Sharing carries business events only, never node Context, pause, continue or stop. Unconnected executions remain isolated even when event names, objects or cloned EventIDs are identical.

## Prepare, connect, start

```csharp
var flowA = engineA.BindContext<InputA>("Start", "Done");
var flowB = engineB.BindContext<InputB>("Start", "Done");
using var a = flowA.Prepare(inputA);
using var b = flowB.Prepare(inputB);
FSMEventSharing.Connect(a, b);                    // void; framework-owned relationship
var started = await FSMEventSharing.StartAsync(a, b);
var runningA = started[0].Execution;
var runningB = started[1].Execution;
runningA.PublishEvent("OrderReady", payload);
await runningA.StopAsync();                       // independent control and true exit
await runningB.StopAsync();
```

Prepare fixes this round's business references and reserves the engine. Explicitly dispose abandoned, unstarted preparations. Disposing a started preparation does not replace Stop/Completion. Business resources remain caller-owned.

Wholly dependency-free engines use `engine.Prepare().WithEntry("Start", "Done")`, without aggregate objects; these can share/start alongside business preparations. For flows without an assembled entry, call `preparation.WithEntry("Start", "Done")` before batch startup. Existing `preparation.StartAsync()` and `StartAsync(startNode, endEvent, isLongRunning)` remain supported, but use the same range barrier: individual starts cannot bypass a connected pending range.

### Configure multiple end events and observers before startup

When coordinated startup needs multiple end events or observers installed before the first node, configure each preparation, then start the complete set. A deliberately failed `StartAsync` is unnecessary:

```csharp
using var a = flowA.Prepare(inputA);
using var b = flowB.Prepare(inputB);
// Named events must already exist in their respective engines.
a.WithEntry("Start", new[] { "Done", "Error" },
    executor => executor.NodeStateChanged += (_, name) => Console.WriteLine(name));
b.WithEntry("Start", new[] { "Done" });
FSMEventSharing.Connect(a, b);
var started = await FSMEventSharing.StartAsync(a, b);
```

- `WithEntry` returns the same preparation and only stores configuration. It does not create an Executor, invoke observers, initialize or run nodes. Observers still run during receiver preparation, before any node in this batch initializes.
- Each call replaces the entire entry, end-event selection and observer. Events and observers are not accumulated; omitting the observer clears it. Existing `WithEntry(startNode, endEvent)` also clears the previous multi-end selection and observer.
- Collections must be non-empty and contain no null events. Names resolve in this engine. The `IEnumerable<FSMEvent>` overload accepts host-owned unregistered end events; startup still prefers same-name local events, without granting new cross-graph event permissions.
- Collections are enumerated and copied outside the sharing lock at configuration time. Later collection edits do not affect the selection; event objects are borrowed, not deep-copied. A lazy enumerable executes caller code and may throw during this step.
- Validation, enumeration or state-check failure preserves the previous valid selection. Starting, started and released preparations reject changes. Successful configuration does not guarantee startup: the complete graph and all pending members are checked again when starting.
- Ordinary single executions still use `StartAsync(...)` directly, sharing configuration logic with `WithEntry`. Legacy preflight/receiver-preparation failures retain configuration for retry, but normal configuration no longer needs a failed call. Failures after node release retain the existing lifecycle and do not promise retrying the same preparation.

## Ranges, notifications and names

- `Connect(params IFSMEventSharingExecution[])` accepts preparations or concrete `FSMExecution` rounds and returns void. No engine-current aliases, retained links, connection using blocks or per-event mappings.
- Overlapping ranges merge; duplicate members and repeated declarations are idempotent. Null/empty input and invalid, released, stopping or exited members are rejected atomically. A singleton is valid.
- Exiting removes only that member, not the remaining relationship. Membership never follows an engine into another round.
- Cross-graph delivery resolves the **full case-sensitive name** in the target graph, then follows its current-node transitions and round's end configuration. Unknown names are ignored. `CheckA.OK` does not match `CheckB.OK`, `checka.OK` or bare `OK`. Modules remain compile-time expansions without aliases.
- EventIDs are graph-local identities, not sharing permission. Foreign/cloned IDs cannot bypass target-local name registration; ordinary and end events follow the same rule. Delivery preserves target bindings and canonical event payloads, using a per-delivery event copy, not a deep copy of business payload objects.
- Each publication fans out directly to a snapshot of joined members, at most once per execution. Receiving does not rebroadcast. A business Publish using the same name, ID or object is a new notification. No reliable delivery, offline replay or elimination of business feedback loops is promised.

## Receiver barrier, failures and retries

Start lists only the pending executions being started, but must cover **all pending members** of every involved range. Duplicate preparations/engines, stale/already started handles, missing entries, changed graphs and incomplete selections are rejected; ordinary startup never replaces an existing execution. A batch may include independent ranges without connecting them.

All checks and rollbackable receiver preparation complete before committing receiver visibility and releasing any node in the batch. Initial synchronous publications can queue at other ready receivers. This neither promises physically simultaneous execution nor requires the target's initial business to execute first. An early business end can finish a target before its initial Run, with zero business execution and normal true exit. Start returns startup results, not a business completion join.

Check/receiver failure releases zero nodes, rolls back temporary receivers and retains valid Prepare handles and engine reservations. Correct and retry with the same handles, or explicitly dispose them. Errors identify the stage, with entry/engine details for omissions and InnerException for root causes. After node release, initialization/business errors use each execution's #19 lifecycle, not transactional batch rollback. Completion still waits for synchronous initialization, cancellation callbacks and non-cooperative nodes to actually return.

## Joining, pause and exit

```csharp
using var b2 = flowB.Prepare(newInputB);           // old b has truly exited
FSMEventSharing.Connect(runningA, b2);           // registers pending membership only
var added = await FSMEventSharing.StartAsync(b2); // also list any other pending members
```

Merging existing active ranges takes effect at Connect. New pending members join only after successful Start receiver preparation. Old members continue without restarting or rebinding. New members receive no pre-join backlog, including after a failed attempt. Failed joining preserves old sharing. A range being prepared rejects concurrent Connect, duplicate Start and pending-handle disposal; retry after the attempt finishes.

Paused members keep receiving into this round's queue, including business end events, and process them after Continue. Stopping/exited members refuse events; stopping one member does not stop others. Receiver/member cleanup belongs inside the true Completion barrier. Finished, Stop or Dispose returning does not permit early engine reuse. Active operations on stale handles fail observably; captured old node routes, old queues and in-flight notifications cannot retarget a replacement round.

See [#20 historical implementation evidence](implementation/20-event-sharing.md) and [final post-contraction acceptance](implementation/22-final-acceptance.md). No parent-child execution, ParallelModule, completion join, persistent messaging or frontend communication panel is introduced.
