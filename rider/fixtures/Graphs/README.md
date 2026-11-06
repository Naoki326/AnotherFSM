# Issue #33 controlled Rider fixtures

This is a test project, not product configuration. `Acceptance.sln` retains Orders and Inventory unchanged. Node `Entry`, factory key `GraphKey`, and C# type `Graphs.GraphWorker` are intentionally different. Neither new key uses `BusinessQuery` or `PayloadTransform`, so #32's fixed Orders ranges/counts must remain unchanged. The factory/constructors/getters/execution bodies write a business-execution marker and throw; analysis must never call them.

## Discovery matrix

| Project item | Physical file |
| --- | --- |
| AdditionalFiles | `Flow/graph.fsm` |
| None | `Flow/**/*.fsm`, `Flow/**/*.FSMScript`, `Items/none.FSMScript` |
| Content | `Items/content.fsm` |
| EmbeddedResource (real file) | `Items/resource.FSMScript` |
| linked None, display `DisplayOnly/linked.FSMScript` | `../SharedGraphs/linked.FSMScript` |

Default None items are disabled. `Loose/graph.fsm`, `Decoys/*` and `DisplayOnly/LinkedChecker.fsm` are **not** project items. Linked imports must use `SharedGraphs/LinkedChecker.fsm`, not the display-directory decoy. `SharedGraphs/LinkedChecker.fsm` is an import dependency, not itself a project item. Discovery of `.FSMScript` does not change the language's `import M` => `M.fsm` rule; the negative directory contains only `ExtensionOnly.FSMScript`.

## Independent expectations

- `graph.fsm` imports `Checker.fsm` as Alias, and Alias is used by Left and Right. `Alias.fsm` is a deliberately wrong same-directory target. Alias references are exactly its two `use` occurrences. Checker Module references include both alias imports and all three alias uses (5).
- `Checker.fsm` imports `Leaf.fsm` relative to its own directory. This is a transitive file dependency only: there is no nested `use` inside Checker (strict runtime forbids it), and no Nested instance/reference is expected. Decoys outside `Flow` must not be guessed. `unopened.FSMScript` uses the same Checker template through another alias, with instance Unopened; it starts closed.
- Positive ordinary Transitions have unique `(source Node, Event)` keys per build scope. Start uses Launch / LaunchEntry / LaunchFirst / LaunchSecond / LaunchDotted / LaunchLocal for its six respective targets, with distinct Branch bindings. Entry uses Next / NextB for PublisherA / PublisherB. Namespace prefixes are retained when checking source/Event identity. These changes do not add/remove Node or Input/Output references.
- Input Entry is the internal Node Entry. Output Done is the Event, not a type or terminal Node. There are four non-declaration Done uses in the template and three external Output uses. Template references therefore have **7 real source positions**, not expanded copies. Left's Output scope has **5** (4 internal + 1 external), with no Right/Unopened instance identity. Left's Input scope has **4** (2 internal source uses + 2 external entry uses). Left instance references have **3** prefix/omitted-entry uses.
- PublisherA has three surviving Done branches; PublisherB has one. Internal Output navigation has **2 publisher Nodes**, not four branch copies. Index 3's first binding to Discarded is overwritten. In `negative/overwrite.fsm`, Done has **no surviving publisher**.
- Signal is both a Node and a separately declared Event. Each has one reference, at different spans of the same Transition. Pulse has no declaration and has **3 introduction/use positions**. Declaration navigation must offer positions rather than inventing a first declaration.
- Zone is a Namespace with two declaration positions and three explicit prefix uses. Plain.Dotted is an ordinary Node name, not a Module Instance. Module `Original as Renamed` registers only Renamed. `use` before a Module is available stays unresolved.
- Branches in Node Branches use `GraphBareKey`, deliberately without branch metadata: error=3, break=4, cancel=5. error and numeric 3 are two locations of the same Branch identity. `MisleadingBranch` is not a navigation target. GraphKey separately provides explicit attribute metadata.
- Missing, cyclic, broken and removed legacy module forms are isolated in `Flow/negative`; trusted local Safe declarations must still work. Private Input, multiple-Input omission, instance-as-source and overwritten publishers must not become certain associations.

## Instrumented no-business-execution observation

`GraphExecutionSentinel.cs` guards `GraphFactory.ctor`, `Current.get`, `CreateNode`, `GetNodeType`, `GetNodeFeatureName`; `Nodes.cs` guards `GraphWorker.ctor`, `BusinessValue.get` (an FSMProperty), `RuntimeKey.get`, `ExecuteEnumerable`, `BareWorker.ctor` / `ExecuteEnumerable`, and `IdentityUse.Node.get`. Entry writes `graph-business-execution.marker` with UTC time, PID, member and source path, then throws. The sandbox's `ANOTHERFSM_ACCEPTANCE_DIR` chooses the run-specific destination; absent that environment variable, the caller's compile-time source path yields the repository `.scratch/issue-33/host/` fallback, not a working-directory guess. Marker-write failure also throws. There are no sentinel calls in static initializers.

The journey checks both marker destinations before readiness, on every tick and at cleanup. It refuses preexisting markers and never deletes them; inspect retained evidence before a new run. It reports the instrumented source files and members, and checks for accidental execution during the preceding #32 journey as well. These types belong only to the controlled Graphs assembly; no StateMachine or actual runtime regression code is altered.

An absent marker is **only evidence about these instrumented entry points**, not proof that all code execution, arbitrary attributes, SDK design-time targets, downloads or credentials are safe. Explicit `dotnet build` / restore of this controlled test project is separately authorized fixture preparation; it must not be misreported as passive IDE analysis. This task does not deliberately invoke business entries to create a marker, and it does not execute a real workflow.

## Real host journey

`rider/tests/host/frontend/DslHostAcceptance.java` is a separate sandbox observer journey, called only after the original `HostAcceptance` succeeds. Its successful callback alone starts the original optional reconnect journey. It uses registered product actions with real Editor/Project contexts, observes source selections and C# declaration lines, reads the public named references JList, exercises Enter/double-click and refresh, and records real chooser models. Enter invokes that verified JList's public InputMap/ActionMap binding and records `public Swing Enter binding`; if absent, only existing public KeyListeners on that component receive local events. This is not a physical OS keypress and never uses Robot/global input or foreground/focus assumptions. Double-click remains local component dispatch; Robot is used only for screenshot capture. RD is used only for readiness/session/version evidence, never as the navigation/ref-search oracle.

The journey also exercises native C# Find Usages (read-only existing observer evidence), a real non-physical unsaved editor, loose-script local fallback, incomplete unsaved syntax, dependency/template/import invalidation, same-title independent Content, stable expired rows, refused stale activation and selective refresh. No product test API or fixture-specific product mapping is permitted.

Evidence goes to the existing `anotherfsm.acceptance.dir`: `dsl-host-acceptance.json`, `dsl-host-acceptance.png`, `dsl-host-failure.png` on failure, and named navigation/reference screenshots. JSON reports every case as pending/running/passed/failed; unvisited cases are not reported as verified. The launcher owner must put its existing acceptance directory under `.scratch/issue-33/host/` for this issue. Preparation/compiler logs already use that directory.

SDK compilation is not a real-host pass. This delegated task does not launch Rider or claim a red/green host execution; the main agent owns the initial red run. Exact reference counts above are specification expectations for review against final core semantics, not observed product results. Chooser row order is not asserted. Runtime/generator regressions and deterministic delayed session/contextVersion packet ordering need separate evidence; #34 cross-project/TFM/external/JSON, #35 completion/docs and #36 performance are not covered here.
