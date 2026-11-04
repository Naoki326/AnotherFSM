# GitHub #5 T3 — Engine editing service completion

## Ownership / files

- `demos/FSMDemo.API/Services/EngineManager.cs`: now `partial`; removed old Export (no export implementation added here).
- `demos/FSMDemo.API/Models/Dtos.cs`: new public-interface DTO contract, removed terminal/mapping fields.
- `demos/FSMDemo.API/Controllers/NodesController.cs`: terminal always returns 400; unsupported instance event rename returns explicit 400.
- `demos/FSMDemo.API/Controllers/EngineController.cs`: Export catches InvalidOperationException -> 400 `{error}` as requested.
- `tests/FSMDemoApiTest/FSMDemoApiTest.csproj`: net10, API ProjectReference, existing xunit/FluentAssertions versions, linked ScriptNodeFactory retaining StateMachineTest.TestInfrastructure namespace.
- `tests/FSMDemoApiTest/EngineEditingFacts.cs`: 27 editing tests/theory cases.
- Original net8 test csproj/TFM, core, frontend, sln/justfile, `EngineManager.Export.cs`, and `ModuleExportFacts.cs` were not edited by this worker.

## Decisions / behavior

- Instance creation/rebuild stages `use Module as Instance;` through core CreateStateMachine using the current editable template, then installs flat nodes/events via a small FSMEngine subclass. No duplicate expansion semantics or runtime module proxy; no OutputEventMap defaults. Lazy registered scripts are loaded by core too.
- External source/target resolution uses ModuleInterfaces.ResolveOutput/ResolveInput. All actual output publishers connect, once per node; wiring never changes Branch. No terminal or mapping fallback.
- Reject ambiguous implicit inputs, private sources/targets, unknown/unpublished outputs, ordinary-node use of internal module events, and source/event conflicts before adding any edge.
- GetConnections projects public edges to `(instance, qualified event, target instance/input)` and deduplicates per interface. Internal template edges retain real node names for internal editing. Multi-input target identity and module public self-loops are preserved.
- Snapshot expanded template edges separately from template edits. Stage and validate every affected instance before removing live nodes. Restore normalized public edges through current publishers; reject edits that remove the last publisher of a connected output or introduce internal/public edge conflicts. Retain instance Pos/Color/FlowID and expanded node Namespace metadata.
- Ownership follows ExpandedNodeNames, not lexical prefixes, so unrelated dotted ordinary nodes are not treated as private or deleted with an instance.
- Old terminal service seam is retained but always false; instance output event rename is disabled. Ordinary create/rename/delete/event/connection editing and Group/Parallel reflection adapters remain covered.

## TDD / verification

All dotnet runs were backgrounded with logs; each bash timeout was 15 seconds.

1. `.scratch/t3-editing-red.log`: original service, 9 expected failures / 15 editing cases before implementation (terminal-only source lookup, implicit multi-input fanout, private node acceptance, default aliases, etc.).
2. `.scratch/t3-editing-red2.log`: added self-loop / Namespace / last-publisher regression tests first; 3 failures / 23 cases before fixes.
3. `.scratch/t3-editing-red3.log`: lazy registration regression added first; 1 failure / 26 cases before fix.
4. `dotnet test tests/FSMDemoApiTest/FSMDemoApiTest.csproj --no-restore`: **28 passed, 0 failed**, including the main agent's ModuleExportFacts. Log: `.scratch/t3-editing-green.log`.
5. `dotnet test tests/StateMachineTest/StateMachineTest.csproj --no-restore`: **89 passed, 0 failed**. Log: `.scratch/t3-core-regression.log`.
6. `dotnet build demos/FSMDemo.API/FSMDemo.API.csproj --no-restore`: **success, 0 errors**. Log: `.scratch/t3-api-build.log`.
7. `git diff --check` for the four edited API files: passed (only git LF/CRLF informational warning).

## Remaining notes / risks

- Existing dependency warning NU1903: Microsoft.OpenApi 2.0.0 / GHSA-v5pm-xwqc-g5wc. Not changed in T3.
- Core tests retain existing FakeNode unused-event warnings.
- This worker verified service/controller behavior and builds, not the real browser workflow; frontend/manual validation belongs to the coordinating workers.
- The editing service assumes edits use its public editing seams. Arbitrary concurrent mutation via the exposed Engine model and editing while an executor runs were existing concerns, not new synchronization work in this ticket.
- Export implementation and roundtrip expansion coverage remain owned by the main agent. No commit/push performed.
