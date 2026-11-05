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

For flows without an assembled entry, call `preparation.WithEntry("Start", "Done")` before batch startup. Existing `preparation.StartAsync()` and `StartAsync(startNode, endEvent, isLongRunning)` remain supported, but use the same range barrier: individual starts cannot bypass a connected pending range.

## Ranges, notifications and names

- `Connect(params IFSMEventSharingExecution[])` accepts preparations or concrete `FSMExecution` rounds and returns void. No engine-current aliases, retained links, connection using blocks or per-event mappings.
- Overlapping ranges merge; duplicate members and repeated declarations are idempotent. Null/empty input and invalid, released, stopping or exited members are rejected atomically. A singleton is valid.
- Exiting removes only that member, not the remaining relationship. Membership never follows an engine into another round.
- Cross-graph delivery resolves the **full case-sensitive name** in the target graph, then follows its current-node transitions and round's end configuration. Unknown names are ignored. `CheckA.OK` does not match `CheckB.OK`, `checka.OK` or bare `OK`. Modules remain compile-time expansions without aliases.
- EventIDs are graph-local identities, not sharing permission. Foreign/cloned IDs cannot bypass target-local name registration; ordinary and end events follow the same rule. Delivery preserves target bindings and canonical event payloads, using a per-delivery event copy, not a deep copy of business payload objects.
- Each publication fans out directly to a snapshot of joined members, at most once per execution. Receiving does not rebroadcast. A business Publish using the same name, ID or object is a new notification. No reliable delivery, offline replay or elimination of business feedback loops is promised.

## Receiver barrier, failures and retries

Start lists only the pending executions being started, but must cover **all pending members** of every involved range. Duplicate preparations/engines, stale/already started handles, missing entries, changed graphs and incomplete selections are rejected; ordinary startup never replaces an existing execution. A batch may include independent ranges without connecting them.

All checks and rollbackable receiver preparation complete before committing receiver visibility and releasing any node in the batch. Initial synchronous publications can queue at other ready receivers. This is not physically simultaneous execution. Start returns startup results, not a business completion join.

Check/receiver failure releases zero nodes, rolls back temporary receivers and retains valid Prepare handles and engine reservations. Correct and retry with the same handles, or explicitly dispose them. Errors identify the stage, with entry/engine details for omissions and InnerException for root causes. After node release, initialization/business errors use each execution's #19 lifecycle, not transactional batch rollback. Completion still waits for synchronous initialization, cancellation callbacks and non-cooperative nodes to actually return.

## Joining, pause and exit

```csharp
using var b2 = flowB.Prepare(newInputB);           // old b has truly exited
FSMEventSharing.Connect(runningA, b2);           // registers pending membership only
var added = await FSMEventSharing.StartAsync(b2); // also list any other pending members
```

Merging existing active ranges takes effect at Connect. New pending members join only after successful Start receiver preparation. Old members continue without restarting or rebinding. New members receive no pre-join backlog, including after a failed attempt. Failed joining preserves old sharing. A range being prepared rejects concurrent Connect, duplicate Start and pending-handle disposal; retry after the attempt finishes.

Paused members keep receiving into this round's queue, including business end events, and process them after Continue. Stopping/exited members refuse events; stopping one member does not stop others. Receiver/member cleanup belongs inside the true Completion barrier. Finished, Stop or Dispose returning does not permit early engine reuse. Active operations on stale handles fail observably; captured old node routes, old queues and in-flight notifications cannot retarget a replacement round.

See the [#20 implementation evidence](implementation/20-event-sharing.md). No parent-child execution, ParallelModule, completion join, persistent messaging or frontend communication panel is introduced.
