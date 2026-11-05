# #17 — independent review and final verification

## Scope and fixed point

- Ticket: https://github.com/Naoki326/AnotherFSM/issues/17 (all ten acceptance criteria).
- Parent #16 remains open. Binding (#18), lifecycle/isolation (#19), sharing/startup (#20), new-binding host migration (#21), and final contraction (#22) are outside this review's acceptance boundary.
- Fixed point: `7f45eeea02fe4601d627a5ec7a92ecd405009baf`.
- Reviewed implementation: `9730a4dfc0363f099b93bdc6df995221f2e6e5be`; command: `git diff 7f45eee...HEAD` in the implementation worktree. Ref existence and nonempty diff were verified before delegation.
- Final documentation-only correction: `f06426e87d84ccffb85cc836607d9e4b1a2e1116`, manually checked against `EditorRuntime` and actual browser behavior by the main agent. No implementation changed after independent review.

## Standards

Independent `code-reviewer` session `01a1a9fd-7993-7598-bbcf-a2fe43983bd8` reviewed every changed file/hunk against repository instructions, domain vocabulary, ADRs, and the code-review skill's complete smell baseline. Report: no evidence-backed hard violations or reportable heuristic smell findings; no speculative findings.

## Spec

Independent `code-reviewer` session `01a1a9fd-b8ac-7598-bbcf-a300eaf628e5` checked #17 against code, tests and existing verification records. Report: no evidence-backed missing/partial requirement, scope creep, or incorrect implementation; no speculative findings. This was read-only review, not a fresh test run.

The main agent checked both reports against the ticket boundary and inspected the relevant changes. Module remains compile-time expansion; ordinary IFSMEngineInjectable engine wiring is retained; legacy transport tombstones reject and do not execute composition. Historical generated ANTLR files are not the active grammar build input. No review finding is left untriaged.

Standards: 0 findings. Spec: 0 findings. Neither axis has a worst issue.

## Main-agent findings and disposition

1. **Accepted and fixed:** the frontend README still advertised editing Group/Parallel definitions. Removed that claim and documented explicit rejection plus non-equivalent Module migration in `f06426e`.
2. **Accepted and fixed:** the frontend README still counted 25 editing/execution operations. Verified the actual interface has 24 operations plus three lifecycle methods; corrected in `f06426e`.
3. **Evidence clarification:** an incremental build's zero-warning output does not mean the repository is warning-free. The main agent's recompilation passed with nullable/unused/package warnings; the implementation record now says so. Unrelated warning cleanup is not part of #17.

## Verification

- Main agent reran solution tests: core 110, API/shared editor 39, host .NET session 23; all passed.
- Main agent reran the positive C# consumer and nine real compiler negatives; all passed.
- Implementer completed frontend tests (12), lint/typecheck, generated-consumer execution, actual Release WASM publication, and real Chromium contracts: backend-free WASM 10 and API 6. The main agent read the resulting test output and checked the contract test code. WASM results are separate from host .NET results.
- Retained command output: [verification-output.txt](verification-output.txt). Detailed acceptance mapping and reproduction commands: [implementation.md](implementation.md).
- Main agent independently loaded the real Release WASM editor, loaded the Module demo, submitted an old Group DSL import, inspected the rejection diagnostic, cancelled the dialog, and observed the unchanged Module graph. The named browser evidence checklist `ticket-17-release-wasm-manual` was audited ready with all five items proven.
- Retained browser frames: [WASM and palette](proofs/wasm-palette.png), [legacy import rejection](proofs/legacy-import-rejected.png), [Module graph preserved](proofs/module-graph-preserved.png).
- The manually started static server was stopped; PID absence and port 5180 connection refusal were verified.

No known unmet #17 acceptance criterion remains. This record does not claim delivery of the parent spec or later tickets.
