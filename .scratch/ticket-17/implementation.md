# Ticket #17 — remove legacy runtime composition

## Boundary and baseline

- Worktree: `C:/work/anotherfsm-ticket-17`; branch: `ticket/17-remove-runtime-composition`.
- Before changes: HEAD, integration worktree HEAD, and merge-base were all `7f45eeea02fe4601d627a5ec7a92ecd405009baf`; `7f45eee` was an ancestor of both branches. No reset, original-worktree edits, push, or issue closure.
- Read #17 (all 10 AC; no comments), #16 including Testing Decisions, #14 comment 6647973766, #13 comment 6648954389; source-worktree AGENTS/domain/issue-tracker/GLOSSARY/ADR-0001/0003; complete TDD SKILL/tests/mocking. Source uncommitted documentation was used as a boundary, not copied into this worktree.
- Confirmed seams: public C#/Fluent/DSL assembly and real execution, actual C# compilation, existing EditorRuntime API/real Release WASM contracts. No new seam approval requested.
- Only #17: no new business binding (#18), execution ownership/lifecycle redesign (#19), sharing/coordinated startup (#20), new-binding host migration (#21), or final cleanup (#22).

## Implementation decisions

- Delete all six GroupNode/ParallelNode shapes, IGroupNode/IParallelNode, FSMDescribe, BaseGroupNode variants, child executors/hooks/control propagation/wrapper exits, reflection helper, and dedicated engine events.
- Keep `EngineBackendNode.Backend`, rebased onto ordinary AsyncEnumFSMNode. Keep historical public `ReinitGroupNode()` for all IFSMEngineInjectable nodes; internal wiring now has an accurate name. Real DSL and Fluent Module executions verify this, not merely object construction.
- Remove node-observation forwarding into executor notifications (the nested-execution path) and GroupScript text-output hook; preserve normal executor observation and controls.
- Actual ANTLR build input is `StateMachine/Engine/StateMachineScript.g4`. Legacy bracket syntax is recognized only by a rejection rule, validated centrally before either state building or transition-only Transform. Diagnostics identify rejected keys/structures, available line/column, Module reuse and its non-equivalence. Both Module expansion paths no longer copy descriptors. The excluded historical `StateMachine/Antlr/` generated files were not used as the build seam.
- Remove supported DTO/manager/Adapter/React composition editing. The old REST route and raw WASM operation are rejection-only tombstones, not compatibility implementations. Legacy create payloads carrying groupDefs cannot silently succeed. Normal node editing and Module projection/round trips remain.
- Migrate generated ATest fixture to an ordinary Wrapper Module with real Branches, qualified events and no runtime proxy. Generated consumers compile, query and execute the migrated Module. Bilingual README, runtime guide and narrow historical glossary/ADR notes reflect removal and non-equivalent migration.

## Red → green history

Each row was completed before beginning the next behavioral slice. Logs below are local generated evidence under `.scratch/ticket-17/` (not committed binaries/logs).

| Slice | Red observation | Green / evidence |
| --- | --- | --- |
| 1: legacy DSL/template rejection | 5 failures: unknown-type generic errors, or bracket descriptions silently accepted | `slice1-red.log` → `slice1-green.log`, 5 passing public build/Try cases |
| 2: removed C# API | Real compiler still accepted GROUP; script reported FAIL | `slice2-red.log` → `slice2-green.log`; final compiler matrix additionally verifies descriptor and base-class removal |
| 3: host operation/DTO | Legacy operation lacked migration diagnostic; ordinary DTO still exposed groupDefs | `slice3-red.log` → `slice3-green.log`, 2 passing shared JSON-boundary cases |
| 4: legacy creation | 3 failures: Group/Parallel creation lacked migration guidance, extra groupDefs was discarded | `slice4-red.log` → `slice4-green.log`, all 23 shared-session tests pass |
| 5: API request binding | Real deserialized legacy CreateNodeRequest returned success | `slice5-red.log` → `slice5-green.log`, API suite passes, including rejection without graph replacement |
| 6: all type keys/source location | 8 failures across generic keys, lowercase key, direct/Fluent assembly and missing source location | `slice6-red.log` → `slice6-green.log`; all six keys and public paths reject explicitly |
| 7: dual Adapter removal and UI migration | Both public runtimes still offered updateGroupDefs | `slice7-red.log` → `slice7-green.log`, 12 frontend tests; real browser editor loop verifies no Group/Parallel palette entries and working ordinary/Module editing |
| 8: transition-only import | Transform silently accepted both old type and bracket description | `slice8-red.log` → `slice8-green.log`, rejection preserves existing transitions |
| 9: nested notifications/text hook | Real executor forwarded an observable node's Legacy.Child notification as an activated runtime node | `slice9-red.log` → `slice9-green.log`; only actual flat execution nodes are reported |

Additional retained/expanded safety nets: real EngineBackendNode execution in DSL and Fluent Module paths (`wiring-execution.log`); existing multi-publisher/deduplicated Branch bindings, qualified event isolation, Module editing, export/import and generated-consumer execution. Old child-lifecycle success tests became explicit rejection tests plus ordinary Module execution tests, not assertions of lifecycle equivalence.

## Final commands and results

Run from this worktree. Every issued bash tool call had a 15–120 second timeout; long publication/browser runs used background logs.

| Command | Result |
| --- | --- |
| `dotnet build AnotherFSM.sln --nologo -p:NuGetAudit=false` | PASS; that final incremental invocation reported 0 warnings/0 errors (`solution-build.log`), not a claim that a full repository rebuild is warning-free |
| `dotnet test AnotherFSM.sln --nologo -p:NuGetAudit=false` | PASS: core 110, API/shared editor 39, host .NET browser session 23 (`all-dotnet-tests.log`) |
| `bash tools/test-removed-composition.sh` | PASS: supported EngineBackendNode consumer compiles/runs and receives its engine; 9 negative real builds (six types, hook, FSMDescribe, BaseGroupNode), CS0246/CS1061 (`compile-final.log`, `compile/*.log`) |
| `dotnet run --project tests/FSMScriptAnalyzerTest` | PASS: generated consumers compile/query; Inline, Embedded and migrated ATest Module flows really execute (`generator.log`) |
| `npm ci --include=dev --prefix demos/FSMDemo.Frontend --script-shell='C:/Program Files/Git/bin/bash.exe'` | PASS; dependencies restored without repository package changes |
| `npm test --prefix demos/FSMDemo.Frontend --script-shell='C:/Program Files/Git/bin/bash.exe'` | PASS: 12 (`frontend-test.log`) |
| `npm run typecheck --prefix demos/FSMDemo.Frontend --script-shell='C:/Program Files/Git/bin/bash.exe'` | PASS (`frontend-typecheck.log`) |
| `npm run lint --prefix demos/FSMDemo.Frontend --script-shell='C:/Program Files/Git/bin/bash.exe'` | PASS (`frontend-lint.log`) |
| `RUNTIME_CONTRACT=true bash tools/build-browser.sh` | PASS: actual Release .NET WASM publication + TypeScript/Vite production build (`release-wasm.log`); Windows npm invocation now explicitly uses Git Bash |
| `MSYS_NO_PATHCONV=1 PORT=5180 VITE_BASE_PATH=/AnotherFSM/ node tools/run-browser-tests.mjs` | PASS: real Chromium, backend-free Release WASM 10 and API 6; final timings 43.3s/13.0s (`browser-run.log`, `.scratch/issue-7/{wasm,api}-browser.log`) |
| `git diff --check` | PASS |

The browser runner first ensures business API port 5079 is absent, verifies WASM without `/api/` or `/hubs/` requests, then starts the real API and runs the same EditorRuntime contract. Raw old REST/WASM operations are also explicitly exercised and fail with Module/non-equivalence diagnostics. Browser processes and servers were stopped by the runner. Real WASM evidence is not inferred from the 23 host .NET tests.

## AC evidence

| AC | Status and executable evidence |
| --- | --- |
| 1 | Met: deleted six shapes and dedicated runtime machinery; negative compilation matrix and flat-only notification execution test |
| 2 | Met: EngineBackendNode ordinary capability preserved; EngineWiringFacts DSL/Fluent real Module executions + compiler positive consumer |
| 3 | Met: 9 compiler negatives, supported solution builds, migrated generated consumers compile and execute |
| 4 | Met: RemovedCompositionFacts, LegacyImport API/session tests, real browser rejected imports including unused templates and six type keys; available source location asserted |
| 5 | Met: actual g4 rejection rule; descriptor copy/output hooks removed; public Transform rejection, DSL/Fluent real execution, Module export/import browser contracts |
| 6 | Met: DTO/manager/dual Adapter/React support removed; both raw transport tombstones reject in real browser, legacy create JSON rejects, editor palette and ordinary graph loop verified |
| 7 | Met: migrated ATest real execution; retained multi-publisher/Branch-binding/qualified-event Module tests and browser round trips; README explicitly documents non-equivalence and no parallel replacement |
| 8 | Met: legacy success tests replaced with rejection and ordinary Module safety nets; 110/39/23 host/core and 12 frontend tests, plus real browsers, remain green |
| 9 | Met: real Release WASM 10 + API 6, backend-free WASM, working Module execution/editing/round trip and rejected legacy requests |
| 10 | Met: bilingual breaking-change guide, generated fixture/consumer execution, narrow demo/runtime/ADR/glossary updates; no source-worktree changes or later-ticket implementation |

Unmet AC: none identified by implementation checks; independent review remains required.

## Environment notes and remaining risks

- Initial npm install inherited `NODE_ENV=production`, omitting development tools (`tsc` unavailable). Resolved with `--include=dev`. Existing build script's implicit Windows npm runner was replaced with explicit Git Bash selection.
- Inherited PORT=30141 and Git Bash path conversion initially prevented serving the expected browser URL. Resolved with explicit PORT=5180 and MSYS_NO_PATHCONV=1; both final browser modes pass. No remaining environment blocker.
- Release prints the existing optional wasm-tools/native-optimization recommendation and Vite third-party SignalR annotation warnings. These are not browser failures; Release WASM really loaded and executed. No AOT/Worker/multithreading was added.
- A first migrated Module stop test, stopping at NodeActivated before Running, timed out once. The retained normal-running stop/restart regression now waits for the public Running notification. No startup/lifecycle race fix was smuggled into #17; early-control/exit guarantees remain #19's responsibility. The old child-setup lifecycle contract is intentionally not claimed as an equivalent Module migration.
- FSMNodeAnalyzerTest is an interactive executable (ReadKey), not an unattended test gate. It was built as part of the solution, not run interactively. The noninteractive generated-script consumer was actually run.
- Breaking removal is intentional; external old C# consumers need source migration. General engine lifecycle/event isolation remains unchanged and is not claimed as newly implemented.

## Review / browser entry points

- Artifacts: `C:/work/anotherfsm-ticket-17/release/browser/` and `release/wwwroot/` (including real `_framework/*.wasm` and the contract harness).
- Serve: `MSYS_NO_PATHCONV=1 PORT=5180 VITE_BASE_PATH=/AnotherFSM/ node tools/serve-browser.mjs`.
- Editor: `http://127.0.0.1:5180/AnotherFSM/`.
- Contract: `http://127.0.0.1:5180/AnotherFSM/test/runtime.html?mode=wasm` (or `mode=api`).
- API for manual comparison: `dotnet demos/FSMDemo.API/bin/Debug/net10.0/FSMDemo.API.dll --urls http://127.0.0.1:5079`.
- Scenarios: six legacy keys/direct create/template import/bracket descriptions and raw groupdefs rejection; existing graph preserved; real Module Branch publishers/qualified events/edit/export round trip; Module execution to Finished; palette absence; normal start/pause/continue/stop and notification cleanup. Automated versions are `test/browser/runtime.spec.ts` and `editor.spec.ts`.
- Before handoff, integration tip was rechecked at `7f45eee`; it is already an ancestor, so no merge commit is necessary. No push/close/next-ticket claim performed.

## Main-agent verification and documentation correction

- Read the main agent's `.scratch/ticket-17/main-verification.log`: the non-incremental recompilation and tests passed (core 110, API 39, host .NET browser session 23). The log includes nullable/override-nullability warnings (for example CS8764, CS8618, CS8602), unused variable/event warnings and a package warning. The earlier incremental invocation's zero-warning output applies only to that invocation; it does **not** establish a warning-free repository. No warning cleanup was attempted in this documentation-only follow-up.
- Read `.scratch/ticket-17/main-compile-verification.log`: all 9 real compiler-negative cases passed again.
- As reported by the main agent, five manual real Release WASM checks completed evidence audit: initialization, no composition entries in the palette, ordinary Module Demo, rejected legacy Group import with non-equivalence/source-position diagnostics, and preservation of the original graph after that failed import. These are main-agent browser observations, separate from the host .NET test results; this follow-up did not repeat browser verification.
- At this update, the independent Spec review has no findings and Standards review is still running; this is not a claim that both reviews are complete.
- Corrected `demos/FSMDemo.Frontend/README.md`: removed the stale Group/Parallel template-editing claim, documented explicit rejection and non-equivalent Module migration, and clarified failed-import graph preservation. Checked the actual `src/runtime/types.ts`: 27 EditorRuntime methods = 3 lifecycle + 24 editing/execution operations, not 25 operations. No implementation changes.
- Integration worktree tip is now `9730a4dfc0363f099b93bdc6df995221f2e6e5be`; merge-base/ancestor checks confirm this branch already contains it. No merge commit, original-worktree edits, push or issue closure are needed for this correction.
