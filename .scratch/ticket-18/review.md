# #18 — independent review and main-agent verification

## Scope and fixed points

- Ticket: https://github.com/Naoki326/AnotherFSM/issues/18 (all fourteen acceptance criteria).
- Parent #16 remains open. Occupancy/true exit/round isolation (#19), business-event sharing/coordinated startup (#20), complete real DSL/Module/factory/generated/API-WASM migration (#21), and transitional API contraction (#22) are not this ticket's missing requirements.
- Fixed baseline: `150013267c14d06b3a8265fe189683d97524776a`; ref and nonempty diff were verified before delegation.
- Original implementation: `ea4a844d70db890b9386de055651bde29a793f7a`.
- Review correction: `3846f5aa883513606e9b743dfe23946a9c873e6d`.
- Full review command: `git diff 150013267c14d06b3a8265fe189683d97524776a...HEAD`; follow-up: `git diff ea4a844d70db890b9386de055651bde29a793f7a..3846f5aa883513606e9b743dfe23946a9c873e6d`.

## Standards

Independent `code-reviewer` session `01a1aa71-3c77-7598-bbcf-a3184fcb37bf` reported **0 hard violations, 1 low nonblocking heuristic finding (Duplicated Code), 0 speculation**.

The default-source matching condition appeared twice in `FSMContextBinding.cs`, once for global default-conflict validation and once for per-node source selection. The reviewer cited the smell baseline rather than claiming a documented-rule violation or current correctness bug.

**Main-agent disposition — accepted and fixed (T1).** Read and compared both original hunks. The same implementer extracted only `MatchesDefault`, preserving the stages' distinct responsibilities. The main agent compared the extracted predicate to the original and confirmed identical Boolean semantics. Independent Standards follow-up confirmed T1 resolved and no new issues in the source-identity fix or public regression tests: **0 remaining findings (0 hard violations, 0 heuristic findings), 0 speculation**. This was read-only review. The main agent re-read the cited final predicate, identity implementation and tests; the evidence supports closing T1.

## Spec

Independent `code-reviewer` session `01a1aa71-764a-7598-bbcf-a31a47794ca5` initially reported **1 P2 implemented-but-wrong finding, 0 scope creep, 0 speculation**.

The per-preparation cache used a property-name-only `Source.Path` string. A base and derived aggregate can declare different public readable properties both named Source with different declaration types, each a unique compatible candidate for a different local interface. Reflection discovers both, but the old cache incorrectly merged them, causing false incompatible-source failure or a wrong object binding.

**Main-agent disposition — accepted and fixed (S1).** Re-read the exact AC and original code. Independently reproduced via real public `BindContext<Input>().Prepare(input)` before the fix: reflection returned both declarations, and preparation rejected Work.Process with actual QueryData instead of ProcessData. The external probe deliberately exited 7 on that observed bug. After the correction, the identical probe prints `PREPARE: success` and exits 0. Retained evidence: [red](main-agent/identity-probe-red.txt), [green](main-agent/identity-probe-green.txt).

The correction uses a complete ordered `(Module, MetadataToken, constructed DeclaringType)` property identity chain; `ReflectedType` is excluded so automatic and explicit discovery of the same inherited property share one fixed object. Path remains diagnostic text. Eight public execution regressions cover hidden declarations, mixed automatic/explicit inherited generic sources, distinct outer paths and distinct generic constructions. The tests observe business results and getter invocations, not internal dictionary contents.

**Independent Spec follow-up: S1 completely fixed; 0 remaining verified findings, no new correctness issues, 0 speculation.** The reviewer checked the identity equality/hash, both cache accesses, public execution tests and unchanged matching semantics. This was read-only static review, not a fresh test execution. The main agent inspected the same changes and independently executed the probe and regression suites.

## Main-agent verification

All commands below were independently rerun after `3846f5a` in `C:/Work/anotherfsm-ticket-18`; outputs retained under [main-agent](main-agent/).

| Command | Result | Output |
| --- | --- | --- |
| `dotnet test tests/StateMachineTest/StateMachineTest.csproj --no-restore --nologo` | **160 passed** | [core](main-agent/final-core.txt) |
| `dotnet test tests/FSMDemoApiTest/FSMDemoApiTest.csproj --no-restore --nologo` | **39 passed** | [API](main-agent/final-api.txt) |
| `dotnet test tests/FSMDemoWasmTest/FSMDemoWasmTest.csproj --no-restore --nologo` | **23 passed**, host .NET only | [host](main-agent/final-host.txt) |
| `dotnet build AnotherFSM.sln --configuration Release --no-restore --nologo` | **0 errors, 64 warnings** in this run | [solution](main-agent/final-solution.txt) |
| `dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj --no-restore` | Generated consumers queried and real Module flows executed | [generated](main-agent/final-generated.txt) |
| `bash tools/test-typed-context.sh` | Positive build/run and all **7 expected real compiler negatives** passed | [compiler](main-agent/final-compiler.txt), [case outputs](compiler/) |
| Public source-identity probe, identical before/after source | Failure before; success after | red/green above |
| `git diff --check` | Passed | no output |

Before the review fix, the main agent also independently passed the original core 152/API 39/host 23 suites and all 9 removed-composition compiler negatives plus its positive consumer. The earlier integration baseline independently passed core 110/API 39/host 23. An initial baseline attempt with `--no-restore` lacked project assets (NETSDK1004); rerunning with restore passed. No code failure is hidden by that environment prerequisite.

Warnings remain, including the pre-existing Microsoft.OpenApi dependency vulnerability warning; warning totals depend on recompilation. This ticket does not claim a warning-free or vulnerability-remediated repository. The unrelated pre-existing `YieldDelay(TimeSpan)` concern is documented in [implementation.md](implementation.md), not silently fixed here.

## Boundaries and delivery state

This is an expand/migrate/shrink intermediate delivery. Data preparation fixes references before node execution but is not yet an occupied lifecycle lease; #19 must attach occupancy/release/true-exit guarantees. Old compatibility entries and necessary internal bridges are inventoried in [implementation.md](implementation.md); new typed bindings do not infer business objects from old Data.

No real Release WASM browser acceptance was run for #18. A solution Browser build and host .NET tests do not substitute for it. Complete production typed host/DSL/factory migration and real browser acceptance remain #21. No parent-spec completion is claimed.

Final review summary: **Standards 0 remaining findings; Spec 0 remaining findings.** Both initial findings were accepted, corrected by the same implementer, independently re-reviewed and individually checked by the main agent. No known unmet #18 AC remains.

Original user workspace `C:/Work/AnotherFSM` remains untouched. Publication, issue closure and remote readback are recorded in the GitHub ticket's delivery comment, not inferred from this local verification report. The integration branch is `implement/context-decoupling`; only #18 is delivered by this session.
