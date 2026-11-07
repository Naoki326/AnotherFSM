# #34 external policy/source boundaries (independent controlled assets)

These are user-owned static test fixtures, not external downloaded source or real runtime
implementations. No factory, getter, attribute constructor or fixture business method is run.
The only executable diagnostic is `MetadataTool`: PEReader/MetadataReader over owned PE/PDB
bytes, with a single fixed invalid-IL patch target. It does not load fixture assemblies.

## Main-owned integration (not changed by this contribution)

- Import `<Import Project="../SourceCases/Host.items.props" />` into
  `Host/BoundaryHost.csproj`. This supplies five controlled binary references and `flow.fsm`.
  Do not add SourceCases library projects to Boundary.sln: these are **binary** origin tests.
- Compile the observer with BOTH `-p:IncludeBoundaryAcceptance=true` and
  `-p:IncludeSourceBoundaryAcceptance=true`. The new `BoundaryPolicySetup.cs` contains a
  separate setup component and a strictly read-only target-specific observer component.
- Main registers/calls:
  `SourceBoundaryAcceptance.start(project, output, boundaryRoot, continuation)`.
  Main's `SourceBoundaryStartup` must not run this concurrently with the reload journey.
  Use the real sandbox launcher for **Boundary.sln**, not the daily Rider configuration.
- Preparation is explicit and separate from plugin analysis. Main's observer preparation is
  `python rider/tools/prepare-acceptance.py --complex --boundaries --source-boundaries`.
  This contribution never installs an observer or launches Rider itself.

## Explicit preparation / settings ownership / execution

```text
python rider/tools/prepare-source-boundary.py prepare
# Main first closes the sandbox frontend AND backend; prepare/install the separate observer.
python rider/tools/prepare-source-boundary.py backup-settings --confirm-sandbox-closed
# Retain the printed backupId. Never create a backup while the backend is live.
python rider/tools/prepare-source-boundary.py serve --port 38434
# Main starts this service as an independently owned process and later stops it.
# Keep 38435 unused for the controlled unavailable-service case.
# Main explicitly launches the isolated Boundary.sln source journey, then handles real legal UI.
# After the journey, main closes frontend AND backend, then:
python rider/tools/prepare-source-boundary.py restore-settings --confirm-sandbox-closed --backup-id <printed-id>
```

`prepare` uses installed net8 reference packs and the already prepared
`Contract/bin/Debug/net8.0/Issue34.StaticContract.dll`; it never builds existing boundary/Complex
projects, installs SDKs, runs business code or fetches packages. Its local-only NuGet.Config
clears package sources; SDK Source Link auto-generation and automatic embedding are disabled.
Do not reprepare DLLs while Rider has them loaded. Logs go under
`.scratch/issue-34/source-boundary/`; the generated asset manifest is
`SourceCases/artifacts/prepared.json`. Both successful and failed preparation evidence is retained.

The settings tool handles ONLY
`rider/.sandbox/config/resharper-host/GlobalSettingsStorage.DotSettings`.
It captures/restores exact bytes or original absence. It refuses a live recorded frontend PID;
main must additionally ensure the backend has exited. No settings contents are printed/exported
from daily profiles. Backup bytes are stored only in the ignored owned asset directory. Main's
post-shutdown restoration receipt is separate from UI results; writing default values is never
claimed as exact layer restoration.

## Explicit setup commands and the real legal dialog

The frontend validates config/system/plugins/log paths, disabled sync/import, actual launch PID,
Boundary.sln, argument-file digest, output path, prepared assets and prelaunch settings backup.
Only then does it write a nonce-correlated `source-policy-authorization.json`. The backend checks
that proof against the launcher, live frontend, actual BoundaryHost module and exact backup.
Without an explicit fresh `source-policy-command.json`, setup makes **no settings writes**.
Only these commands are accepted, once per command UUID:

- `disable-decompile-and-downloads`: typed DecompileMethodBodies=false and PDB
  Download/UseSymbolFiles/SourceLinkV3=false.
- `enable-decompile-local-only`: only DecompileMethodBodies=true; the three PDB switches stay false.
- `request-consent`: invokes public `DecompiledSourceLegalNotice.CheckAndAskUser` on the host's
  primary thread with PSI locks released. It never directly sets consent. Only the **real host
  Yes/No dialog** may persist user approval. Main observes it, captures it and explicitly clicks
  Yes for these user-owned fixtures, or No to decline. Nothing automatically clicks/accepts it.

The frontend writes `source-consent-request.json` with runId, commandId and frontendPid.
After observing/capturing the actual dialog and clicking Yes, main writes this witness under
that same output directory (replace all values with actual evidence):

```json
{
  "runId": "from-source-consent-request",
  "commandId": "from-source-consent-request",
  "frontendPid": 123,
  "clickedYes": true,
  "ownedFixtureOnly": true,
  "dialogCaption": "actual observed host caption",
  "dialogScreenshot": "actual-legal-dialog.png",
  "dialogScreenshotSha256": "actual image SHA256"
}
```

This is explicitly main-supplied evidence, not a self-generated approval. The frontend also
requires the host API's real accepted return, original consent=false and an independent later
consent=true snapshot. Already-accepted consent is observed as an approved host state, without
claiming a new UI click in that run; retain any earlier real legal witness separately.
The legal step has a 600s deadline. If main has not handled the real dialog, the journey does
not auto-dismiss or approve it; main must handle any remaining modal before continuing/shutdown.

## What is asserted, and what is NOT

- Typed raw body=false is NOT a pass: two independent SDK samples must show effective=false
  AND the exact decompiler provider inapplicable for every actual compiled target. If Rider's
  feature override keeps it enabled, the disable case is pending with the observed snapshot.
- Registered ActionManager actions originate in real DSL editors. Product notifications must
  retain exact compiled binding/metadata and `source match unverified`. After observing the
  action result, a later read-only snapshot must show all policy fields and identities unchanged.
- Allowed navigation requires an actual selected editor at the expected class declaration,
  an SDK-generated decompiler header, the product's visible `Decompiled code`/`Metadata view`
  label and an actual window screenshot. Dispatch alone is never navigation success.
- `offline` has a matching portable PDB containing ONLY loopback Source Link for that source.
  A bounded loopback connection refusal establishes only that owned source endpoint's
  unavailability, not whole-machine offline. No external source-fetch provider is called.
- `unauthorized` has matching portable PDB/loopback Source Link. The server returns local HTTP401
  without receiving any credentials. This is controlled source availability evidence, NOT a
  real private-feed authorization test. The frontend probes only loopback, without redirects.
- `mismatch` has a real portable-PDB SHA256 checksum mismatch against the actual local candidate.
  Its metadata `FSMNodeSourceAttribute` constructor argument points to that candidate. This is
  a **static provenance witness** declared in the fixture assembly, not a claim about runtime
  attribute/module identity or source-generator execution. No getter/constructor is invoked.
- `nosymbols` has no adjacent PDB, no CodeView record and no embedded source. This is controlled
  evidence insufficiency; the journey never upgrades a path/Source Link/PDB into verified source.
- `broken` retains readable PE metadata/type keys but RenderProbe contains reserved invalid IL.
  Malformed IL alone does NOT prove an engine failure. An actual product reason naming
  `decompiler presentation failed` / `decompiler produced no presentation`, with effective policy,
  accepted consent and applicable exact provider, is recorded as provider presentation failure
  only; it does not distinguish engine generation from provider initialization/positioning/cache
  failure. Alternatively, a real SDK-generated editor must match the prepared binary MVID and
  explicitly report `RenderProbe` as `ISSUE: unable to decompile the method.` The unchanged-metadata
  IL patch and that host error are a method-level decompilation failure witness, with type metadata
  retained; they do not claim a whole-engine exception or cancellation. Successful resilient rendering
  without that explicit error fails the negative assertion. Consent/options refusal is not failure.

`source-boundary-acceptance.json` remains partial while external-source-fetch/whole-machine/private
conditions or post-shutdown restoration are unverified. Continuation means no failed assertion,
not an all-AC pass. A missing importer/backup/real host setup remains pending and performs no setup.
#35 documentation/completion and #36 performance are outside this task.

Contribution-level preparation originally covered observer/Java compilation and PE/PDB inspection
only; it did not stand in for acceptance. Main integration has since completed the actual 12-step
Rider policy/source journey, visible owned-fixture consent, method-level host failure witness,
loopback-service checks and exact post-shutdown settings restoration. See the permanent scoped
reports and safety limits in `rider/evidence/34/README.md`; the reports, not preparation, prove these actions.
