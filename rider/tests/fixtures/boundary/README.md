# #34 independent boundary fixture

NOT part of `rider/fixtures/Complex/Complex.sln`; do not alter those fixtures to run this journey.
No business program, factory, getter, constructor or registration is ever to be invoked.
`Contract/BoundaryContract.csproj` links the existing **static symbol fixture** source only;
it is not the real AnotherFSM runtime. `Host/Uses.cs` is guarded by the same execution sentinel.

## Integration (main owns launcher/registration/preparation)

1. Prepare the existing controlled Complex binary `artifacts/dll/Issue34.HostLoadedNodes.dll`
   beforehand. Explicitly prepare this new `Boundary.sln` with installed net8/net10 targeting
   packs, including restore assets for Host/net10 (Host is initially net8). Preparation is a
   separate opt-in action, never done by the driver. Do not install SDKs or use remote feeds.
   This directory's restore source is local-only; it has no package dependencies.
2. Compile the observer with `-p:IncludeBoundaryAcceptance=true`. Java entry point:
   `ComplexBoundaryAcceptance.start(project, output, boundaryRoot, onSuccess)`.
3. Sequentially, in a separate controlled sandbox launch, explicitly open **Boundary.sln**.
   The existing launcher's live PID, fixture, argument-file hash, output path, config/system/
   plugins/log paths and disabled import/sync must match. Never open in a daily Rider profile.
   Reuse the same read-only `HostObserver`; it supplies real native descriptor/range samples.
   No StartupActivity is introduced here; main must call the entry point for this solution.
4. The journey saves only its own `Host/BoundaryHost.csproj` through a real editor and waits
   for real automatic Rider reevaluation. It removes a Compile item, removes a ProjectReference,
   then switches net8 -> net10, restoring between cases. Expected temporary compiler errors in
   Uses.cs are intentional. Do not enable external automatic builds/run configurations.
5. Read `complex-boundary-acceptance.json`. Continuation means completion without failed
   assertions/cleanup, **not full AC pass**. Pending policy/source/trust cases remain explicit.
   A setup timeout terminates pending and does not call onSuccess. Check the report, not the callback.

## Limits

No policy settings are written, no consent is accepted, no source/PDB/Source Link provider is
selected, no sources downloaded, no private server authenticated and no IDE launched by these
files. Unaccepted consent is tested only if the observed effective provider is otherwise
applicable. Effective disabled decompilation is tested only if actually observed, not inferred
from raw settings. PDB-download disabled is narrowly labelled; it is not a claim that every
host source-download mechanism is disabled. Main can independently establish additional policy
states in its verified sandbox, but must restore exact setting layers outside this observer.

Real generator failure/cancellation, offline/private-feed 401, source-version mismatch,
insufficient symbols and real untrusted/unloaded projects remain pending. `loose.fsm` verifies
only absence of an evaluated consuming project; it is NOT evidence of a host unload/trust action.
Native stale-row checks select the exact public Usage through UsageView, correlate its real tree
selection, then exercise double-click and the registered EditSource command. They do not claim a
native Enter shortcut or a generic tree-toggle as navigation. If exact public Usage/tree selection
cannot be confirmed, activation stays pending. Swing delivery is component-local, not physical OS input.

This implementation was SDK/Java compiled only; it has NOT run in a real Rider window.
