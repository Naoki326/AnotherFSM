# #18 implementation / verification

## Baseline and scope
- Only worktree `C:/Work/anotherfsm-ticket-18`, branch `ticket/18-typed-context`.
- Initial HEAD exactly `150013267c14d06b3a8265fe189683d97524776a`; ancestor check passed; initial tree clean.
- Read AGENTS, GLOSSARY, ADR-0001/0003, supplied ticket/spec/decisions 9–12, TDD SKILL/tests/mocking. No user documents copied from other worktrees.
- No push, issue mutation, claim or subsequent ticket implementation.
- #19 owns occupancy/release/true exit/round isolation; #20 event sharing/coordinated start; #21 complete real DSL/factory/generated/host aggregate pairing; #22 transitional-entry removal. These are not claimed complete here.

## Confirmed seams and vertical plan
Parent spec Testing Decisions already confirms (no further interview):
1. Public core assembly + real execution: observe business results, triggers, state and diagnostics. No private dictionary/cache tests or mocks of internal collaborators.
2. Real C# compiler positive/negative cases for source compatibility, aggregate input and assignment restrictions.
Existing core/API/host suites and affected-consumer builds are regression gates, not real Release WASM acceptance.

Implemented vertically: local Context/shared execution → qualified override → duplicate rejection → stable source declarations → factory metadata → eager preparation/diagnostics → control/trigger/Yield → expression restrictions → restart fencing → no public rebinding → complete conflict checks → legacy rejection → stale-plan checks → no boxing → malformed metadata → diagnostic completeness → malformed configuration → assembled entry convenience. Tests for already-supported scanning and family behavior extend these slices without claiming a new red where existing behavior was already green.

## Public design and decisions
- `FSMEngine.BindContext<TInput>([startNode, endEvent,] configure)` creates a reusable whole-engine assembly plan. Relationships may live in the aggregate's static Configure method. The plan holds types/property paths, never an execution's business instances.
- `FSMContextBindings<TInput>` provides `Default<TContext>`, `DefaultSource`, `Node<TContext>`; expressions are existing public property paths rooted in the input, not projections.
- `FSMContextFlow<TInput>.Prepare(input)` eagerly resolves every required source into fixed references. Separately named `PrepareDynamic` supplies contextual runtime host diagnostics without weakening typed `Prepare` overload resolution.
- `FSMContextPreparation.StartAsync()` uses assembled entry/end names, or the explicit-name overload selects a local entry. This is data-only preparation, NOT an occupied lifecycle handle; #19 must reuse this resolution path when attaching occupancy/release.
- `AbstractFSMNode<T>.Context : T` is getter-only. Core generic/non-generic families declare requirements through `IFSMContextNode[<T>]`. Internal binding target prevents public rebinding. Non-generic families explicitly declare no business dependency.
- All factories using the new assembly step share one validator: factory GetNodeType(typeKey), actual CLR node type, reflected generic requirement and reported requirement must agree. Validation covers all engine nodes regardless of reachability. No second DSL binding algorithm introduced.
- `FSMExecutionControl` contains cancellation/pause only; `TriggerEvent` is independent of business Context. All Yield actions now accept Control, never business Data.
- Typed transfers never copy upstream Context/Data. Continuing preserves the incoming business event. Fresh execution installs fresh control/trigger state for all nodes and invokes InitBeforeStart after all references are bound.
- Legacy RestartAsync is fenced from business-dependent nodes and typed executors; it cannot silently reuse typed bindings.
- Node-set mutation invalidates a plan and requires reassembly. No graph/engine occupancy guarantee is claimed by this data-level check.

## AC mapping
| AC | Production behavior | Public evidence |
|---|---|---|
| 1–2 local interfaces / composition / single input | Local T Context, static aggregate Configure, assembly-time entry option, per-run Prepare | LocalInterfacesShareTheAggregateMemberThroughRealExecution; QualifiedNodeExceptionWinsOverTheSharedDefaultAndSurvivesTransition; AnAssembledEntryNeedsOnlyOneAggregateInstanceForEachExecution |
| 3 priority / duplicates | Node > interface default/shorthand > unique automatic source; reject duplicates even when hidden/unused | InvalidRelationshipsAreRejectedBeforeAnExecutablePlanExists; ExplicitDuplicateDefaultsAreInvalidEvenIfNoNodeCurrentlyUsesThatInterface; ExplicitInterfaceDefaultOverridesUniqueCompatibleAutomaticSource; AmbiguityListsBothDeclaredSourcesAndChosenNullDoesNotFallBack |
| 4 scan / expressions | Declared direct public-readable instance non-indexed properties; explicit nested property paths only | AutomaticMatchingUsesOnlyDirectPublicInstanceReadableNonIndexedPropertyDeclarations; ExplicitExistingNestedPathIsAllowedButAutomaticSearchDoesNotRecurse; IllegalSourceExpressionsHaveAssemblyDiagnosticsWithNodeAndRequirement; ValueTypeSourcesCannotSilentlyTurnBorrowingIntoABoxedBusinessCopy |
| 5 source assignment / static typing | Reject ordinary public set; accept init/private set/getter-only; compiler-enforced local source/input/assignment constraints | OrdinaryPublicSetIsRejectedAtAssemblyButStableDeclarationsAreAccepted; `tools/test-typed-context.sh` positive and seven compiler negatives |
| 6 whole engine / metadata | Validate names, exact actual requirement, generic metadata and factory type; unreachable nodes not exempt | FactoryMetadataIsCheckedAgainstTheActualNodeBeforeAssemblyCompletes; MissingOrNonNodeFactoryMetadataCannotMasqueradeAsNoBusinessDependency; ActualGenericRequirementAndReportedMetadataMustAgreeWithDiagnostics; AnUnreachableActualRequirementIsNotExemptAndRuntimeExtraInterfacesAreNotGuessed |
| 7 fixed preparation | Resolve before initialization/run, cache source identity per preparation, not at first node | PreparationFixesIdentityBeforeFirstNodeAndSharesLiveMemberUpdates; MultipleInterfaceViewsOfOneSourceKeepTheSameReferenceEvenWithAnUnstableGetter |
| 8 null/type/getter failures and diagnostics | Eager full plan, no fallback, qualified names/requirements/paths/declarations/actual types/root cause; no invented DSL positions | NullAndDynamicWrongInputsFailWithIdentityAndRootCauseBeforeAnyNodeRuns; UnreachableNodeSourcesAreStillRequiredWhenStartingAtALocalEntry |
| 9 no dependency vs missing business | Explicit no-dependency core families; valid but unready fields left to nodes | NonGenericFamiliesDeclareNoDependencyAndYieldControlsDoNotNeedAnEmptyBusinessObject; ValidBusinessObjectsWithUnreadyFieldsAreHandledByBusinessNodesNotTheBinder |
| 10–11 core families / Yield / trigger | Base/Enum/AsyncEnum, generic and non-generic, independent Control and TriggerEvent; continuation is not a business trigger | TypedExecutionFacts real execution; pause/continue preserves exact event identity/payload, transfer updates trigger, fresh execution starts null |
| 12 initialization / ownership | Contract-based InitBeforeStart; no reflective clearing/copy/rollback/disposal of caller objects | YieldPauseContinuePreservesBusinessAndTriggerWhileNewExecutionInitializesPrivateState; LoopReentryKeepsBusinessMutationsAndFrameworkDoesNotDisposeCallerResources |
| 13 evidence | Production public execution, assembly diagnostics, real C# compiler; no private caches/dictionaries asserted | red/green table below; final regression logs |
| 14 consumers / docs | Keep temporary legacy bridge; complete solution builds; existing API/host/generated execution regressions; bilingual new guide | README(.EN), docs/typed-context(.EN).md; final build/regression logs |

## Preserved red/green evidence
Commands were run from this worktree; every bash timeout was explicitly 15 or 120 seconds. Long command output is on disk. `dotnet test tests/StateMachineTest --no-restore --filter FullyQualifiedName~Typed` is the standard slice command (first slices filtered TypedContextFacts).

| Files | Red cause → green result |
|---|---|
| 01-red/green.txt | Compiler: missing local business Context and BindContext → 1 real execution passes |
| 02-red/green.txt | Compiler: missing typed relationships → 2 executions pass |
| 03-red/green.txt | 3 invalid relationship cases incorrectly accepted → all 7 pass |
| 04-red/green.txt | Ordinary public set accepted → all 8 pass (during implementation, missing FSMException inner-exception constructor also fixed before green) |
| 05-red/green.txt | Factory metadata mismatch accepted → all 9 pass |
| 06-red/green.txt | Compiler: dynamic preparation absent → all 11 pass, eager diagnostics and identity |
| 07-red/green.txt | Compiler: Control/TriggerEvent absent → all 13 pass with real Yield pause/continue |
| 08-red/green.txt | 6 illegal expression diagnostics missing node/requirement → all 22 pass |
| 09-compiler.txt | Initial script expected CS0029; real compiler gave CS0266 for object-to-interface. Corrected expected diagnostic, then all six original negatives/positive passed. This was a test expectation correction, not a production type-safety failure. |
| 10-red/green.txt | Typed executor silently accepted legacy restart → all 24 pass |
| 11-red/green.txt | Real compiler unexpectedly allowed public BindContext call → internal binding target, seven compiler negatives and 24 production tests pass |
| 12-red/green.txt | Duplicate defaults unused by nodes accepted → all 25 pass |
| 13-red/green.txt | Legacy Data could start typed nodes without a plan → all 29 pass |
| 14-family-regression.txt | Already-supported behavior verification: all 31 pass (includes real non-generic Base/Enum/AsyncEnum and loop/ownership) |
| 15-red/green.txt | Nodes added after assembly silently skipped → all 32 pass |
| 16-red/green.txt | Struct source silently boxed/copied → all 33 pass |
| 17-red/green.txt | Missing/non-node metadata accepted or leaked NullReferenceException → all 35 pass |
| 18-red/green.txt | Multiple requirements lacked qualified diagnostic → all 37 pass |
| 19-red/green.txt | Null node name treated as a default; null expression leaked NullReferenceException → all 39 pass |
| 20-red/green.txt | Compiler: assembled-entry overload absent → all 42 typed cases pass |

`compiler/positive.txt`, `run.txt`, `final-positive.txt` and BAD_*.txt contain real compiler outputs. BAD_BINDING=CS0266/CS1662; BAD_INPUT=CS1503; BAD_INIT=CS8852; BAD_PRIVATE=CS0272; BAD_READONLY/BAD_CONTEXT_REBIND=CS0200; BAD_BIND_ENTRY=CS1061. All negative builds must fail with the expected diagnostic; no runtime exception is offered as a static guarantee.

## Transitional entries / consumers (for #21/#22)
- `IFSMNode.Context`, non-generic AbstractFSMNode.Context, FSMNodeContext/FSMNodeContext<T>/As<T>/Data: legacy control/Data surface, not typed business resolution.
- FSMExecutor constructors and RestartAsync overloads: existing no-business consumers retained; typed nodes/executors reject these restart/data paths.
- Internal `StartContextAsync` installs the existing IFSMNode control/trigger wrapper as a minimal execution bridge. It does NOT read Data. New business bindings are separate fixed values; transfers do NOT overwrite them. `NewExecutionUsesTheNewExplicitAggregateRatherThanResidualBusinessBindings` also verifies different explicit objects across sequential executions.
- Existing DSL and Fluent builders still return FSMEngine; their complete host aggregate pairing, source-generator APIs and all production factory registrations move in #21. BindContext is already the single reusable validation boundary.
- Existing `demos/DemoNodes`, `demos/FSMDemo.Runtime`/API/Browser hosts, generated script/node consumer projects and their test infrastructure retain no-business legacy entry points. No final business compatibility promise added.
- #22 removes these old public entries once migrated. No old Group/Parallel capability restored; ordinary Module and engine injection remain intact.

## Final verification / artifacts
Final counts: **152 core tests (43 typed-context/execution cases), 39 API tests, 23 host .NET tests**, all passed. Typed cases additionally passed three repeated Release runs. Complete solution Release build: **zero errors** (warnings remain; the final incremental build reports 3). Seven compiler negatives, compiler positive/run, nine removed-composition negatives, and real generated Module execution passed. `git diff --check` passed. Integration tip was merged (no-op, Already up to date) at `150013267c14d06b3a8265fe189683d97524776a`. Whole-solution Release build includes core, DemoNodes, API, shared Runtime, Browser, both analyzers and generated consumer projects. The interactive FSMNodeAnalyzerTest program is build-only (do not run ReadKey unattended). `dotnet test` on the executable analyzer projects was not counted as a test; actual FSMScriptAnalyzerTest execution is separately recorded.

- `solution-release-build-final.txt`: complete affected-consumer Release build, zero errors; existing warnings remain.
- `core-regression-final.txt`, `api-regression-final.txt`, `host-regression-final.txt`: explicit regression suites.
- `generated-consumer-regression-final.txt`: real generated Module consumers compiled, queried and executed.
- `compiler-final.txt`: positive run/build plus all seven negative compiler cases.
- `removed-composition-regression.txt`: all nine removed-API compiler regressions still reject (also positive build/run).
- `integration-merge.txt`: `git merge implement/context-decoupling` (tip remains baseline; Already up to date).
- Build artifacts (ignored, not committed): `StateMachine/bin/Release/Naoki.AnotherFSM.1.1.15.nupkg`, library/consumer binaries, `demos/FSMDemo.Browser/bin/Release/` build outputs. Browser Release build is NOT a real browser publish/runtime acceptance claim.

## P2 follow-up: source declaration identity (review finding)
- Review independently reproduced a false source collision: a base and derived aggregate declare different public read-only properties both named `Source`, with unique IQuery/IProcess declaration types. The original name-only preparation key incorrectly reused the first object for the second node.
- Confirmed public seam remains BindContext → Prepare → StartAsync; do not test private dictionaries. First add a hidden-property execution regression and preserve red before changing production code.
- Minimal identity plan: compare the full ordered property declaration chain by module + metadata token + constructed DeclaringType. Ignore ReflectedType so automatic inherited-property discovery and explicit expressions identify the same declaration; retain closed generic declaring types so different constructions are not conflated. Name-only Path stays diagnostic text.
- Then verify mixed automatic/default/node rules evaluate the same inherited generic property once, and different outer property paths to a shared terminal declaration stay independent. Run necessary core/API/host regressions, generated execution and complete Release build.
- Existing main-agent rerun files under `compiler/*.txt` are preserved; do not rerun the script that overwrites that directory. Initial review reports and independent probe remain untouched.
- Red `21-source-identity-red.txt`: the new public hidden-property execution test failed in Prepare with actual QuerySource incompatible with required IProcess, matching the independent probe. Green `21-source-identity-green.txt`: the identical test completes both nodes with independent business results after the fix.
- Production change: ContextSource now compares/hashes its full ordered `(Module, MetadataToken, DeclaringType)` property chain, and Prepare keys resolved references by ContextSource rather than Path. No public API, scanning, precedence or lifecycle changes. `ReflectedType` does not participate; constructed generic `DeclaringType` does.
- `22-source-identity-regression.txt`: 8 public execution cases pass: hidden declarations; the same inherited generic property via automatic, interface-default, node and shorthand rules is evaluated once (also using a public expression explicitly built from the base declaration's PropertyInfo against automatic derived-type reflection); different outer paths sharing a terminal declaration stay independent; different constructed generic members execute with their declared sources. Getter counts/business results are observed, not internal caches.
- Standards review: no hard violations; accepted the one low non-blocking duplication finding. Extracted only `MatchesDefault` to share the identical predicate. Global default-conflict validation and per-node source selection remain distinct stages. `23-default-matching-regression.txt`: all 11 priority/ambiguity/duplicate/conflict cases pass after extraction.
- Bilingual typed-context guides now state declaration-chain identity and diagnostic-only name paths. Original execution evidence and initial review reports are retained.
- Follow-up final verification: complete `AnotherFSM.sln` Release build succeeds with zero errors; `p2-core-regression.txt` **160/160**, `p2-api-regression.txt` **39/39**, `p2-host-regression.txt` **23/23** pass. `p2-generated-consumer-regression.txt` confirms real generated Module execution. All **51** typed cases also pass three repeated Release runs (`p2-stability-1/2/3.txt`). These are the updated counts after the original 152-test delivery above.
- Compiler rerun logs were neither restored nor overwritten: `p2-preserved-compiler.sha256` / `p2-compiler-preservation-check.txt` verify byte preservation. The nine already-modified `compiler/*.txt` files are deliberately left uncommitted for the main agent's evidence commit; this fix does not rerun the overwriting compiler script because no public typing signature changed.
- Follow-up risk note: build still emits existing warnings, including NU1903 for the existing Microsoft.OpenApi 2.0.0 dependency. This dependency/security upgrade is not part of the source-identity fix. No new known residual identity or matching issue remains; the existing lifecycle/migration/Release-WASM boundaries and pre-existing YieldDelay(TimeSpan) concern below are unchanged.

## Remaining boundaries and risks
- No known unfulfilled #18 behavior in the covered contract. This is intentionally an expand/migrate/shrink intermediate state, NOT parent #16 completion.
- Preparation is data-only; concurrent starts, old handles, engine/node occupancy, exit cleanup and round isolation remain #19. Independent event sharing/coordinated start remains #20. Do not use the preparation result as an occupied execution lease.
- Complete typed production factory/DSL/generated/API-WASM migration and real Release browser contract are #21; no real Release WASM test was run for this ticket. Existing host .NET tests are labeled accordingly.
- Static inspection noticed a pre-existing unrelated `YieldDelay(TimeSpan)` self-recursive construction / duration defect. It was not changed or executed here (integer-delay and pause behavior are exercised); do not interpret these regressions as proving that overload correct.
- No push, issue closure, remote publication or later-ticket claim performed.
