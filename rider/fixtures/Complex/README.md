# Issue #34: controlled complex-project fixtures

## Boundary and preparation

Only complex projects, dependency origins, host identity and TFM identity are covered here.
This does **not** implement plugin navigation, #35 completion/docs, #36 performance, a host test,
a runtime regression, or acceptance of the whole issue. `../Acceptance.sln` is independent and unchanged.

From the repository root:

```text
python rider/tools/prepare-complex-fixtures.py
# Optional, if the two pinned framework packages are in another EXISTING local feed:
python rider/tools/prepare-complex-fixtures.py --offline-framework-feed C:/local/offline-feed
# Optional, if the pinned WASM packages are in another EXISTING local cache/feed:
python rider/tools/prepare-complex-fixtures.py --offline-wasm-cache C:/local/nuget-cache
# Optional full second configuration:
python rider/tools/prepare-complex-fixtures.py --configuration Release
# Optional additional standalone real net8 WebAssembly SDK build (must succeed):
python rider/tools/prepare-complex-fixtures.py --probe-net8-wasm
```

The script is **explicit preparation**, not new passive plugin execution. It builds three controlled
DLL producers, builds/packs one controlled NuGet producer, builds the real WebAssembly SDK project
`StaticWasm` separately and in `Complex.sln`, builds all Matrix hosts/TFMs, queries their actual SDK,
four dependency origins and linked project items, queries TFM-exclusive project-item properties, and
verifies two expected build failures outside the solution. It never launches Rider, loads business assemblies,
constructs attributes, calls a factory/getter/business entry, runs a workflow, or invokes run/test/publish.
No plugin/core/host-test files are involved. Logs and per-command results are under
`.scratch/issue-34/fixtures/<UTC-and-id>/`; `summary.json` includes source/output SHA-256 and marker state.
Each child command has a 110-second default timeout (configurable only within 15..120 seconds).

Prerequisites: installed .NET 8/10 SDK/reference packs, plus existing `NETStandard.Library 2.0.3` and
`Microsoft.NETCore.Platforms 1.1.0` nupkgs from the installed SDK's `sdk/NuGetFallbackFolder` or a supplied
offline feed. Additionally StaticWasm uses existing cached nupkgs `Microsoft.NET.Sdk.WebAssembly.Pack
10.0.9`, `Microsoft.NETCore.App.Runtime.Mono.browser-wasm 10.0.9`, and `Microsoft.NET.ILLink.Tasks 10.0.8`.
MatrixWasm/net8 additionally requires `Microsoft.NET.ILLink.Tasks 8.0.27` and
`Microsoft.NETCore.App.Runtime.Mono.browser-wasm 8.0.28`. These net8 inputs are mandatory in the default
preparation, preferring the already populated local fixture feed. The script stages only these seven
pinned prerequisites, never all cache contents, recording
the original path, source/destination SHA-256 and equality readback in summary.json. The default WASM
cache is the existing NUGET_PACKAGES directory or ~/.nuget/packages; --offline-wasm-cache overrides it.
No package is repacked/reconstructed; missing nupkgs fail with searched paths. The script never downloads
external packages or source, authenticates, installs a workload/SDK, or reads credential files. `RestoreConfigFile` points
only at this fixture's `NuGet.Config`, audit is off, certificate revocation is offline, credentials plugins are disabled for preparation,
and package-source mapping allows only the local `artifacts/feed`. SDK-injected installed `library-packs`
may appear in assets source listings but is excluded by mapping. Missing prerequisites fail closed.

### Separately authorized main-agent net8 package preparation

The previous genuine net8 SDK attempt failed with NU1102 for Mono browser-wasm 8.0.28. Main has now
resolved that exact gap via an explicitly authorized, official-only NuGet PackageDownload restore,
**separate from this script and from plugin execution**. Main's restore log is
`.scratch/issue-34/net8-pack-fetch/restore.log` (successful restore, 13.42 seconds). Its existing package is:

```text
.scratch/issue-34/net8-pack-fetch/packages/microsoft.netcore.app.runtime.mono.browser-wasm/8.0.28/microsoft.netcore.app.runtime.mono.browser-wasm.8.0.28.nupkg
```

Main copied it to `Complex/artifacts/feed/microsoft.netcore.app.runtime.mono.browser-wasm.8.0.28.nupkg`.
The original and feed copy were read back with identical SHA-256:
`98cde7182c0530e6a021a32fb0e1bb2de99c77d9f8247c0f4eff6f174d921b9a`.
On a fresh offline checkout, main must first provide that existing nupkg in this feed (or the explicit
local cache/feed). The script verifies the pinned hash, stages/reads it locally, and fails closed with
searched paths or a hash mismatch if unavailable/changed. **Default prepare never adds a download or
network fallback**, and does not invoke main's fetch project or any package-fetch command. Main's
separate authorization does not authorize this script to install SDKs/workloads or access any source.

A reproducible **separately authorized official-only preparation** is signed in as
`Tooling/Net8RuntimeFetch/Fetch.csproj` and its `NuGet.Config`. The project contains only the net10 SDK
context and one exact PackageDownload (no PackageReference, business source or custom target).
It overrides the inherited Complex offline source/config only for this standalone project; it is
**not in Complex.sln and is never invoked by prepare-complex-fixtures.py**. Run the following only
when the owner explicitly authorizes this official dependency fetch, from the repository root, with
an external 120-second command timeout. Do not build/run/publish this project or install any workload:

```bash
mkdir -p .scratch/issue-34/net8-pack-fetch
env NUGET_PLUGIN_PATHS= NUGET_CREDENTIALPROVIDERS_PATH= \
    NUGET_CERT_REVOCATION_MODE=offline DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true \
    dotnet restore rider/fixtures/Complex/Tooling/Net8RuntimeFetch/Fetch.csproj \
    --packages .scratch/issue-34/net8-pack-fetch/packages \
    --configfile rider/fixtures/Complex/Tooling/Net8RuntimeFetch/NuGet.Config \
    -p:NuGetAudit=false --nologo > .scratch/issue-34/net8-pack-fetch/restore.log 2>&1
```

The config clears inherited sources and maps **only** Microsoft.NETCore.App.Runtime.Mono.browser-wasm
to the official NuGet source. No private source or authentication is added. After an exit-code-zero
restore, explicitly copy the nupkg and verify both source and destination against the pinned hash:

```bash
python - <<'PY' > .scratch/issue-34/net8-pack-fetch/copy-and-hash.log
import hashlib
from pathlib import Path
import shutil
source = Path('.scratch/issue-34/net8-pack-fetch/packages/microsoft.netcore.app.runtime.mono.browser-wasm/8.0.28/microsoft.netcore.app.runtime.mono.browser-wasm.8.0.28.nupkg')
target = Path('rider/fixtures/Complex/artifacts/feed') / source.name
expected = '98cde7182c0530e6a021a32fb0e1bb2de99c77d9f8247c0f4eff6f174d921b9a'
def checksum(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()
if checksum(source) != expected:
    raise RuntimeError('Official package source SHA-256 mismatch; refusing copy')
target.parent.mkdir(parents=True, exist_ok=True)
shutil.copyfile(source, target)
if checksum(target) != expected:
    raise RuntimeError('Copied package SHA-256 mismatch')
print(source, expected)
print(target, expected)
PY
```

Then use the ordinary offline preparation commands above. This helper recipe is not a new plugin
capability or an implicit network fallback; ordinary preparation still requires existing local files.
The checked-in recipe is a reusable equivalent of main's successful scratch fetch. This fixture agent
has statically checked the helper and read back the existing package, not re-run its network restore.

SDK 10 on this machine prefers net8 servicing packs not installed locally. `Directory.Build.targets`
uses the **real installed 8.0.5** targeting packs for net8 and disables targeting-pack acquisition.
Runtime-pack acquisition remains disabled except for the real WebAssembly SDK, which must restore its
actual Mono browser-wasm runtime pack from the local-only feed. This is NuGet's package acquisition,
not an authorization for HTTP sources or workload installation.
On another offline machine with different packs, explicitly pass
`-p:Issue34Net8TargetingPackVersion=<installed-version>` to a manual fixture build (or install nothing
and report the missing prerequisite). No fabricated reference framework or retargeting is used.

## Project and binding matrix (expected static associations, not observed IDE results)

| Project | Real SDK / target | DSL / meaning |
| --- | --- | --- |
| StaticContract | Microsoft.NET.Sdk / netstandard2.0 | Deliberately minimal static contract only |
| ProjectNodes | Microsoft.NET.Sdk / netstandard2.0 | library.fsm positives; negative.fsm rejection cases |
| HostA | Microsoft.NET.Sdk / net8.0 | sources.fsm covers all four origins, negative.fsm covers ambiguity/invisibility |
| HostB | Microsoft.NET.Sdk / net10.0 | host.fsm has its own key and intentionally unavailable HostA/DLL/package keys |
| MultiTarget | Microsoft.NET.Sdk / net8.0;net10.0 | framework.fsm changes binding with the selected TFM |
| StaticWeb | Microsoft.NET.Sdk.Web / net8.0, OutputType Library | site.fsm + wwwroot; real Web SDK/static assets, no server entry point |
| StaticBrowser | Microsoft.NET.Sdk / net10.0 | browser.fsm + wwwroot, SupportedPlatform browser, browser assembly annotation; retained auxiliary fixture, NOT WASM AC |
| StaticWasm | Microsoft.NET.Sdk.WebAssembly / net10.0, Exe, browser-wasm | surface.fsm: WasmEntry / WasmSurfaceKey / Issue34.StaticWasm.SurfaceWorker; WasmBuildNative=false |
| MatrixDependency | Microsoft.NET.Sdk / netstandard2.0 | Dedicated MatrixProjectKey dependency; no reference from original hosts |
| MatrixLibrary | Microsoft.NET.Sdk / netstandard2.0 | Linked source-matrix.fsm, all four origins |
| MatrixPlain | Microsoft.NET.Sdk / net8.0;net10.0 | Linked source-matrix.fsm, all four origins in each TFM |
| MatrixWeb | Microsoft.NET.Sdk.Web / net8.0;net10.0, OutputType Library | Real static Web SDK, all four origins in each TFM |
| MatrixWasm | Microsoft.NET.Sdk.WebAssembly / net8.0;net10.0, Exe, browser-wasm | Real WASM SDK, all four origins, WasmBuildNative=false, sentinel Main |
| NegativeIdentity | Microsoft.NET.Sdk / net8.0 | identity.fsm rejects fake contract/attribute identities |
| ImpostorContract | Microsoft.NET.Sdk / netstandard2.0 | Same StateMachine CLR names, intentionally different assembly |

StaticBrowser is retained unchanged as a **static browser-context class library**, NOT the WebAssembly
AC fixture. StaticWasm is the real **Microsoft.NET.Sdk.WebAssembly** executable SDK project, using the
same SDK/net10/Exe/WasmBuildNative=false setup as demos/FSMDemo.Browser, without its business/runtime
project dependencies. SDK evaluation must report UsingMicrosoftNETSdkWebAssembly=true and
RuntimeIdentifier=browser-wasm. Build emits StaticWasm.dll, dotnet.native.wasm, JS/static assets and
WebCIL StaticWasm.wasm. Program.Main is a sentinel which throws (and writes an explicitly configured
marker); compilation must never invoke it. No run, publish, native/AOT rebuild or browser launch occurs.
This is not a Microsoft.NET.Sdk.BlazorWebAssembly project; no Blazor UI or server is claimed. StaticWeb
also is not a running server. Real Rider support/loading still needs host evidence, not SDK compilation.

The real repository StateMachine project is not referenced: even with a compatible TFM it introduces
ANTLR/package/build-tool dependencies. `Issue34.StaticContract` exports the names `StateMachine.IFSMNode`
and `StateMachine.FSMNodeAttribute` for static symbol analysis only. Its IFSMNode shape is deliberately
minimal and **not runtime/API compatible** with AnotherFSM. Do not deploy it or mix it into Acceptance.

| Origin in HostA/sources.fsm | Node | Factory Key | C# type |
| --- | --- | --- | --- |
| Current project | CurrentEntry | CurrentHostKey | Issue34.HostA.InvoiceWorker |
| Visible ProjectReference | ProjectEntry | ProjectLookupKey | Issue34.ProjectLibrary.DependencyWorker |
| Host-visible local DLL Reference/HintPath | BinaryEntry | HostDllKey | Issue34.BinaryLibrary.BinaryWorker |
| Local PackageReference | PackageEntry | LocalPackageKey | Issue34.PackageLibrary.ParcelWorker |

DLL producers DllNodes/TwinLeft/TwinRight and PackageNodes are intentionally **not** in `Complex.sln`.
A solution-wide source scan must not masquerade as binary/package visibility. HostA consumes prepared
DLLs, not producer ProjectReferences; HostB consumes neither DLLs nor the package. Binary assemblies
have embedded PDB/source information; actual source-vs-metadata IDE navigation remains to be verified.

## All-source project/TFM matrix

The independent Matrix hosts add **seven consuming project/TFM contexts x four sources = 28 pairs**;
HostA's coverage is not extrapolated to another SDK or TFM. Each host explicitly references
StaticContract and MatrixDependency by ProjectReference, the prepared DllNodes DLL by HintPath, and
exact local Issue34.LocalNodes [1.0.0] by PackageReference. MatrixDependency itself references only
StaticContract. Original HostA/HostB/ProjectNodes reference graphs and negative keys are unchanged.
No original host references a Matrix project, so MatrixCurrentKey/MatrixProjectKey must not leak there.

| Shared script Node | Key | Type | Actual source/assembly |
| --- | --- | --- | --- |
| MatrixCurrentEntry | MatrixCurrentKey | Issue34.SourceMatrix.CurrentWorker | ../SharedComplex/SourceMatrix.cs, independently compiled into MatrixLibrary/MatrixPlain/MatrixWeb/MatrixWasm |
| MatrixProjectEntry | MatrixProjectKey | Issue34.MatrixDependency.ProjectWorker | MatrixDependency/Nodes.cs, MatrixDependency.dll (netstandard2.0) |
| MatrixBinaryEntry | HostDllKey | Issue34.BinaryLibrary.BinaryWorker | artifacts/dll/Issue34.HostLoadedNodes.dll, producer DllNodes |
| MatrixPackageEntry | LocalPackageKey | Issue34.PackageLibrary.ParcelWorker | local Issue34.LocalNodes/1.0.0, assembly Issue34.PackagedNodes.dll |

All four Matrix hosts link **one physical** ../SharedComplex/SourceMatrix.cs and source-matrix.fsm as
Linked/SourceMatrix.cs and Linked/source-matrix.fsm. Keep project/assembly/consuming-TFM context when
grouping the same script or C# range; CurrentWorker has four host assemblies and seven consuming contexts.
The shared C# SourceWitness.Types and MoreTypes getters each guard first and then contain typeof for
all four origins. This supplies **two distinct native C# type-reference ranges per origin** even after
physical-range deduplication, so a native Find Usages journey has genuine source starting points and
multiple candidate usages. Getters, constructors and Main must never be called to discover any type.
Actual Find-page retention, grouping, deduplication and UI result counts require main/host evidence;
compilation and static reference positions do not prove a successful IDE journey.

Preparation builds the 15-project Complex.sln, then queries each Matrix inner TFM's actual ProjectReference,
Reference/HintPath, PackageReference, Compile/None items and SDK properties. It fails on an incomplete
origin graph, wrong SDK/RID/TFM or missing shared item. summary.source_matrix records all seven contexts;
source_matrix_context_count=7 and source_matrix_origin_pair_count=28 are recorded explicitly;
source/output hashes include every Matrix assembly, MatrixDependency and MatrixWasm's native/WebCIL
WASM assets. No DLL/package producer source is added to the solution to manufacture source visibility.
The original constructor/getter sentinels and the additional main-agent typeof witnesses are untouched.

Optional --probe-net8-wasm additionally builds FailureCases/Net8WasmProbe with the **real WebAssembly
SDK**, net8.0, Exe and WasmBuildNative=false. The historical path is retained outside Complex.sln, but
it is now a **positive** offline build: a nonzero exit fails preparation rather than being accepted as
an expected NU1102 or ignored capability failure. Its command, exit code, warning/error codes, actual
SDK/RID properties and output hashes are recorded under summary.net8_wasm_probe and artifacts.
Regardless of this optional flag, MatrixWasm/net8 and net10 are required positive matrix cells, with
all four origins and per-TFM native/WebCIL WASM outputs. No ordinary SDK replacement, TFM downgrade,
package/workload download, authentication, run or publish occurs. UI support remains main's separate
verification; neither the resolved package gap nor a successful SDK build is a UI pass.

## Identity, alias and generic cases

- TwinLeft/TwinRight contain the same `Issue34.SameNames.CollisionWorker` CLR name, with distinct
  LeftAssemblyKey/RightAssemblyKey. Their DuplicateWorker also shares AmbiguousAssemblyKey. Preserve
  both assembly identities; the ambiguous key must not silently select either assembly.
- HostA uses `extern alias LeftAssembly/RightAssembly` and explicit `typeof` references as compile-time
  witnesses. Both references also have the global alias, so ordinary visibility is not removed.
  ProjectNodes uses attribute/interface `using` aliases, including a named Key overriding the ctor key.
- Warehouse.ShelfWorker is nested but concrete (positive); Warehouse.UnregisteredShelfWorker has no
  registration (negative). BufferWorker<T> and GenericWarehouse<T>.SlotWorker are open (negative).
- TextBufferWorker : BufferWorker<string> is concrete (positive). `typeof(BufferWorker<string>)` in
  the guarded witness getter must not manufacture an open generic factory registration. TextRecord
  has a constructed generic **non-node** base, and AbstractIntBufferWorker has a constructed generic
  base but is abstract (negative). The DSL uses registration keys, not C# generic expressions.
- OrdinaryRecord, AbstractWorker, invalid named null Key and the ignored ctor fallback are negative.
  NegativeIdentity combines foreign attribute, foreign IFSMNode and foreign pair via an alias-only
  reference, plus ValidWorker as a real-contract control. Matching CLR spelling alone is insufficient.

## Linked/shared hosts and multi-targets

`../SharedComplex/dual-host.fsm` is one physical script with Node SharedEntry / Key SharedHostKey.
HostA links it directly as `Linked/dual-host.fsm`. HostB imports `SharedComplex.projitems`, displaying it
as `Shared/dual-host.fsm`. Both compile the same SharedBinding.cs with different constants:
HOST_A => Issue34.HostA.Shared.LinkedInvoiceWorker;
HOST_B => Issue34.HostB.Shared.SharedDispatchWorker.
The projitems is a real MSBuild shared-item import, not a separate legacy .shproj or an executable host.
Do not collapse the two bindings by physical script path, display path or key alone.

MultiTarget/framework.fsm uses FrameworkBindingKey: net8 => LegacyRouteWorker; net10 => ModernRouteWorker.
EightEntry/Net8OnlyKey and TenEntry/Net10OnlyKey are positive only in the matching TFM, negative in the
other. The same source/script paths do not mean the same compilation context or declaration target.

MultiTarget/only-eight.fsm is the canonical TFM-exclusive host-probe item: default None is removed first,
then None is included only for TargetFramework=net8.0. The earlier net8-only.fsm path is retained as a
separate probe with the same conditional membership, so existing readers are not broken. Both use the
existing Net8OnlyKey and add no types or changes to framework.fsm. Preparation queries the SDK's
evaluated None items for both inner TFMs and requires one match per script for net8 and zero for net10. This is project-item evidence only; Rider
IProjectFile.Properties.GetBuildAction(module.TargetFrameworkId) returning UNDEFINED and consumer
behavior must still be verified by the host owner, not inferred from solution-wide file discovery.

## Sentinels and reproducible failures

StaticContract/Contract.cs guards the real fixture attribute constructors and Key getter/setter,
ControlledNode ctor/Execute/BusinessValue getter, BusinessFactory ctor/Current getter/CreateNode/
GetNodeType, and BusinessEntry.Run. HostA guards its own node ctor/Execute and alias witness getter;
DLL/package producers guard their business entries. StaticWasm guards Program.Main and its own node
ctor/Execute. The impostor attribute throws as well.
No static or module initializer calls these sentinels.

`ANOTHERFSM_COMPLEX_EXECUTION_MARKER` is an explicitly supplied **absolute file** destination. On entry,
the central guard appends UTC/member information if configured, then throws; without it, it throws
without writing anywhere. Preparation chooses a fresh run-local marker and checks before/after each
child process, never deletes a marker and never deliberately invokes a sentinel. A missing marker is
only evidence for instrumented entries, not a proof against all possible code execution or networking.
An external IDE journey would need to set its own absolute marker destination; none is launched here.

`FailureCases/MissingDll` references an intentionally absent copy of the real producer DLL and uses
BinaryWorker, causing CS0246 (plus unresolved namespace). `FailureCases/MissingPackage` requests exact
version [0.0.0-negative] of the local package, causing NU1102 even after preparation. Both are excluded
from Complex.sln; the preparation script requires their nonzero exits and expected diagnostics.
DSL negatives are compilable project content, not a reason for the positive SDK build to fail.

Generated outputs: each controlled project's bin/obj; `artifacts/dll/Issue34.{HostLoadedNodes,TwinLeft,
TwinRight}.dll`; `artifacts/feed/Issue34.LocalNodes.1.0.0.nupkg` and the two copied framework packages;
`artifacts/packages` is an isolated restore cache. Re-preparation refreshes only the controlled local
package cache, preserving edited source, existing logs and markers. Matrix host outputs are hashed
for every requested TFM, alongside MatrixDependency.dll. MatrixWasm emits the same categories of real
WASM build assets as StaticWasm. StaticWasm additionally emits
bin/<configuration>/net10.0/{StaticWasm.dll,StaticWasm.runtimeconfig.json,dotnet.native.wasm} and
obj/<configuration>/net10.0/webcil/StaticWasm.wasm; hashes are recorded from actual queried TargetPath.
The SDK deliberately omits browser-wasm from the output directory, despite that being the actual RID.
No generated binaries are committed.

The cache rmtree is followed immediately by both lexists and parent-directory listing readbacks before
any restore can recreate it; summary.cleanup.local_package_cache records both. Each build records the
owned Popen PID, parent PID and command. On timeout, native Python subprocess calls taskkill /PID /T /F
(no MSYS argument conversion), waits/reaps the owned process, then queries tasklist for the root and
reported terminated child PIDs. Cleanup is not verified unless all reported PIDs are absent; command,
PID, return codes and readback output are retained. Shared compilation/node reuse are disabled for
controlled builds. The timeout branch is fail-closed and must not be reported exercised by a normal
successful build; any deliberate cleanup probe may run only a controlled sleeping tool process, never
a business assembly or sentinel.

## Evidence versus remaining verification

Verified by explicit offline preparation: SDK project compilation (including real WebAssembly SDK/RID,
Mono runtime assets and WebCIL output), both MultiTarget outputs and TFM-exclusive evaluated None item
membership, DLL copy, local NuGet pack/restore, expected compiler/restore failures, unchanged source hashes within each run,
and absence of the preparation marker. Exact commands, outputs and hashes live in the run summary/logs.
The installed fallback Microsoft.NETCore.Platforms 1.1.0 nupkg produces NU3005 (invalid compressed
signature entry) during the DllNodes preparation build. It still builds successfully; the original
package is copied byte-for-byte and hashed, not repaired, repacked or silenced. This is a known local
prerequisite warning, not a clean package-signature validation result; see command warning_codes/logs.
Expected associations above are an independent fixture oracle, **not a plugin pass**. Real Rider project
loading, dependency/alias visibility, decompilation/source navigation, selected host/TFM behavior,
negative identity rejection and failure degradation still require a separate authorized host journey.
