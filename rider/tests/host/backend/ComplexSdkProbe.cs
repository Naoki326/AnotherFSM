using System.Text.Json;
using AnotherFSM.Rider;
using AnotherFSM.Semantics;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.Application.Progress;
using JetBrains.ReSharper.Feature.Services.Occurrences;
using JetBrains.ReSharper.Psi.ExtensionsAPI;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Impl.Search.Operations;
using JetBrains.ReSharper.Resources.Shell;

namespace AnotherFSM.Acceptance;

/// <summary>Issue #34, real consuming SDK modules and public queries only. Never navigates or executes fixture code.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ComplexSdkProbe
{
    private readonly List<object> checks = new();
    private readonly List<object> observations = new();
    private readonly List<object> abandonedAttempts = new();
    private readonly DateTime started = DateTime.UtcNow;
    private Timer? timer;
    private int queued;
    private bool detected, failed, running;
    private volatile bool completed;
    private long stableEpoch = -1;
    private string? directory;
    private string[] waiting = Array.Empty<string>();
    private readonly string caseId = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_CASE") ?? "all";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ComplexSdkProbe(Lifetime lifetime, ISolution solution, IPsiServices psi, ScriptDocuments scripts,
        ScriptIndex index, BindingSettings settings, BridgeQueries bridge)
    {
        directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        timer = new Timer(_ => {
            if (!lifetime.IsAlive || (!detected && DateTime.UtcNow - started > TimeSpan.FromMinutes(3))) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.ComplexSdkProbe", () => {
                try
                {
                    if (running) return;
                    var modules = scripts.Modules();
                    // Do not pollute the independent Acceptance.sln regression's evidence.
                    if (!modules.Any(module => Name(module) == "HostA" && (BindingSettings.ProjectPath(module) ?? "").EndsWith("/Complex/HostA/HostA.csproj", StringComparison.OrdinalIgnoreCase))) return;
                    detected = true;
                    CheckMarker();
                    if (caseId != "all")
                    {
                        FocusedReadiness(psi, modules);
                        return;
                    }
                    var required = new[] { ("HostA", "net8.0", "CurrentHostKey"), ("HostB", "net10.0", "OtherHostOnlyKey"),
                        ("ProjectNodes", "netstandard2.0", "ProjectLookupKey"), ("MultiTarget", "net8.0", "FrameworkBindingKey"),
                        ("MultiTarget", "net10.0", "FrameworkBindingKey"), ("StaticWeb", "net8.0", "StaticSiteKey"),
                        ("StaticBrowser", "net10.0", "BrowserCanvasKey"), ("StaticWasm", "net10.0", "WasmSurfaceKey"),
                        ("NegativeIdentity", "net8.0", "IdentityControlKey"),
                        ("MatrixLibrary", "netstandard2.0", "MatrixCurrentKey"),
                        ("MatrixPlain", "net8.0", "MatrixCurrentKey"), ("MatrixPlain", "net10.0", "MatrixCurrentKey"),
                        ("MatrixWeb", "net8.0", "MatrixCurrentKey"), ("MatrixWeb", "net10.0", "MatrixCurrentKey"),
                        ("MatrixWasm", "net8.0", "MatrixCurrentKey"), ("MatrixWasm", "net10.0", "MatrixCurrentKey"), ("MatrixDependency", "netstandard2.0", "MatrixProjectKey") };
                    var missing = new List<string>();
                    var catalogs = new Dictionary<string, IReadOnlyList<NodeType>>();
                    var caches = solution.GetComponent<IPsiCachesState>();
                    if (!psi.Files.AllDocumentsAreCommitted || !caches.IsInitialUpdateFinished.Value || !caches.IsPrimaryIdle.Value)
                        missing.Add("Host PSI not committed / initialized / primary-idle");
                    foreach (var (name, tfm, key) in required)
                    {
                        var found = modules.Where(module => Name(module) == name && BindingSettings.Framework(module) == tfm).ToArray();
                        if (found.Length != 1) { missing.Add(name + "/" + tfm + ": expected one primary module, got " + found.Length); continue; }
                        var catalog = TypeCatalog.Read(found[0]);
                        catalogs[found[0].GetPersistentID()] = catalog;
                        // Host readiness must not depend on the binding under test being correct.
                        if (!found[0].SourceFiles.Any(file => file.IsValid() && file.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
                            missing.Add(name + "/" + tfm + ": no committed C# project source view");
                    }
                    waiting = missing.ToArray();
                    if (missing.Count != 0) { stableEpoch = -1; Waiting(); return; }
                    // Independent host readiness at two unchanged epochs; failed binding controls are assertions below.
                    if (stableEpoch != index.Epoch) { stableEpoch = index.Epoch; waiting = new[] { "Awaiting second complete sample at unchanged epoch" }; Waiting(); return; }
                    var epoch = index.Epoch;
                    IPsiModule Module(string name, string tfm) => modules.Single(module => Name(module) == name && BindingSettings.Framework(module) == tfm);
                    var a = Module("HostA", "net8.0"); var b = Module("HostB", "net10.0");
                    IReadOnlyList<NodeType> Catalog(IPsiModule module) => catalogs[module.GetPersistentID()];
                    var aggregateFactory = Shell.Instance.GetComponent<FinderSearchDomainVisitorFactory>();
                    Check("actual SDK aggregation factory registration", aggregateFactory is FsmFinderSearchDomainVisitorFactory,
                        aggregateFactory.GetType().FullName ?? "unknown");
                    observations.Add(new { name = "ready-modules", epoch, modules = required.Select(item => {
                        var module = Module(item.Item1, item.Item2);
                        return new { project = item.Item1, framework = item.Item2, id = module.GetPersistentID(),
                            tfmIdentity = module.TargetFrameworkId.UniqueString, catalog = Catalog(module) };
                    }).ToArray() });
                    foreach (var (name, tfm, key) in required)
                    {
                        var module = Module(name, tfm);
                        var scope = psi.Symbols.GetSymbolScope(module, true, true);
                        var binding = Bind(key, Catalog(module));
                        Check("positive control: " + name + "/" + tfm + "/" + key, binding.State == BindingState.Determined, new {
                            binding, catalog = Catalog(module),
                            contracts = scope.GetTypeElementsByCLRName("StateMachine.IFSMNode").Select(type => new {
                                clr = type.GetClrName().FullName, module = type.Module.GetPersistentID(),
                                references = psi.Modules.GetModuleReferences(module).Where(reference => ReferenceEquals(reference.Module, type.Module))
                                    .Select(reference => reference.ExternAliases).ToArray() }).ToArray(),
                            attributes = scope.GetTypeElementsByCLRName("StateMachine.FSMNodeAttribute").Select(type => type.Module.GetPersistentID()).ToArray()
                        });
                    }
                    foreach (var (key, clr) in new[] { ("CurrentHostKey", "Issue34.HostA.InvoiceWorker"),
                        ("ProjectLookupKey", "Issue34.ProjectLibrary.DependencyWorker"), ("HostDllKey", "Issue34.BinaryLibrary.BinaryWorker"),
                        ("LocalPackageKey", "Issue34.PackageLibrary.ParcelWorker"), ("NestedLookupKey", "Issue34.ProjectLibrary.Warehouse+ShelfWorker"),
                        ("ClosedBufferKey", "Issue34.ProjectLibrary.TextBufferWorker"), ("NamedOverrideKey", "Issue34.ProjectLibrary.NamedWorker") })
                    {
                        var binding = Bind(key, Catalog(a));
                        Check("HostA origin/alias/concrete: " + key, binding.State == BindingState.Determined && binding.Candidates.Single().ClrName == clr, binding);
                        if (binding.State == BindingState.Determined)
                        {
                            var identity = binding.Candidates.Single(); var type = TypeCatalog.Resolve(psi, identity);
                            Check("full identity roundtrip: " + key, type != null && TypeCatalog.Identity(type, a, identity.Alias, identity.Construction) == identity, identity);
                            Check("consuming context preserved: " + key, identity.Consumer == a.GetPersistentID() && identity.Context == a.TargetFrameworkId.UniqueString, identity);
                            FinderCheck(psi, scripts, type!, key, new[] { "/HostA/sources.fsm" });
                            var cached = index.Catalog(a);
                            Check("indexed consuming catalog agrees with committed SDK snapshot: " + key,
                                cached.ToHashSet().SetEquals(Catalog(a)), new { cached, snapshot = Catalog(a), fresh = TypeCatalog.Read(a) });
                            observations.Add(new { name = "dependency provenance (not verified source)", key, identity,
                                definingModuleKind = type?.Module.GetType().FullName, isCompiledElement = type is ICompiledElement,
                                assembly = (type?.Module as IAssemblyPsiModule)?.Assembly.AssemblyName.FullName,
                                sourcePaths = type?.GetSourceFiles().Select(ScriptDocuments.Id).ToArray(),
                                classification = "SDK location/assembly evidence only; presence of a path/PDB is not verified external source" });
                        }
                    }
                    foreach (var key in new[] { "OtherHostOnlyKey", "OpenGenericKey", "OpenOuterKey", "PlainNotNodeKey", "AbstractOnlyKey",
                        "ClosedNonNodeKey", "AbstractClosedKey", "UnregisteredShelfWorker", "ForbiddenFallbackKey", "IgnoredCtorKey" })
                        Check("HostA refuses " + key, Bind(key, Catalog(a)).State == BindingState.Unresolved, Bind(key, Catalog(a)));
                    foreach (var key in new[] { "CurrentHostKey", "HostDllKey", "LocalPackageKey", "LeftAssemblyKey" })
                        Check("HostB cannot borrow HostA dependency: " + key, Bind(key, Catalog(b)).State == BindingState.Unresolved, Bind(key, Catalog(b)));
                    var duplicate = Bind("AmbiguousAssemblyKey", Catalog(a));
                    Check("same CLR name in two assemblies remains ambiguous", duplicate.State == BindingState.Ambiguous
                        && duplicate.Candidates.Count == 2 && duplicate.Candidates.Select(id => id.Module).Distinct().Count() == 2, duplicate);
                    var left = Bind("LeftAssemblyKey", Catalog(a)).Candidates.Single(); var right = Bind("RightAssemblyKey", Catalog(a)).Candidates.Single();
                    Check("extern aliases retain definition identity", left.ClrName == right.ClrName && left.Module != right.Module
                        && left.Alias.Split(',').Contains("LeftAssembly") && right.Alias.Split(',').Contains("RightAssembly"), new { left, right });
                    observations.Add(new { name = "alias-definitions", definitions = new[] { (identity: left, alias: "LeftAssembly"), (identity: right, alias: "RightAssembly") }
                        .Select(item => new { item.alias, identity = item.identity, assembly = (TypeCatalog.Resolve(psi, item.identity)!.Module as IAssemblyPsiModule)!.Assembly.AssemblyName.FullName }).ToArray() });
                    foreach (var (name, tfm) in new[] { ("MatrixLibrary", "netstandard2.0"), ("MatrixPlain", "net8.0"), ("MatrixPlain", "net10.0"),
                        ("MatrixWeb", "net8.0"), ("MatrixWeb", "net10.0"), ("MatrixWasm", "net8.0"), ("MatrixWasm", "net10.0") })
                    {
                        var consumer = Module(name, tfm);
                        var catalog = Catalog(consumer);
                        var source = scripts.Roots(consumer).Single(file => ScriptDocuments.Id(file).EndsWith("/SharedComplex/source-matrix.fsm", StringComparison.OrdinalIgnoreCase));
                        foreach (var key in new[] { "MatrixCurrentKey", "MatrixProjectKey", "HostDllKey", "LocalPackageKey" })
                        {
                            var binding = Bind(key, catalog);
                            var identity = binding.State == BindingState.Determined ? binding.Candidates.Single() : null;
                            var target = identity == null ? null : TypeCatalog.Resolve(psi, identity);
                            Check("source matrix binding: " + name + "/" + tfm + "/" + key, target != null && identity!.Consumer == consumer.GetPersistentID()
                                && identity.Context == consumer.TargetFrameworkId.UniqueString && (key == "MatrixCurrentKey" ? identity.Module == consumer.GetPersistentID()
                                    : key == "MatrixProjectKey" ? target.Module.ContainingProjectModule is IProject project && project.Name == "MatrixDependency"
                                    : target is ICompiledElement), binding);
                            if (target == null) continue;
                            var found = new Collector();
                            psi.SingleThreadedFinder.FindReferences(target, psi.SearchDomainFactory.CreateSearchDomain(source), found, NullProgressIndicator.Create());
                            var rows = found.Snapshot().Select(result => new FsmOccurrenceProvider().MakeOccurrence(result)).OfType<RangeOccurrence>().ToArray();
                            Check("source matrix actual restricted Finder: " + name + "/" + tfm + "/" + key,
                                rows.Length == 1 && ((IOccurrence)rows[0]).IsValid && rows[0].GetDocumentRange().GetText() == key
                                    && StringComparer.OrdinalIgnoreCase.Equals(ScriptDocuments.Id(rows[0].SourceFile), ScriptDocuments.Id(source)),
                                rows.Select(row => new { file = ScriptDocuments.Id(row.SourceFile), text = row.GetDocumentRange().GetText(), display = row.GetDisplayNameText() }).ToArray());
                        }
                    }
                    var negative = Catalog(Module("NegativeIdentity", "net8.0"));
                    foreach (var key in new[] { "ForeignAttributeKey", "ForeignInterfaceKey", "ForeignPairKey" })
                        Check("foreign contract/attribute identity refused: " + key, Bind(key, negative).State == BindingState.Unresolved, Bind(key, negative));
                    foreach (var (name, tfm, key, clr) in new[] {
                        ("StaticWeb", "net8.0", "StaticSiteKey", "Issue34.StaticWeb.PageWorker"),
                        ("StaticBrowser", "net10.0", "BrowserCanvasKey", "Issue34.StaticBrowser.CanvasWorker"),
                        ("StaticWasm", "net10.0", "WasmSurfaceKey", "Issue34.StaticWasm.SurfaceWorker"),
                        ("MultiTarget", "net8.0", "FrameworkBindingKey", "Issue34.MultiTarget.LegacyRouteWorker"),
                        ("MultiTarget", "net10.0", "FrameworkBindingKey", "Issue34.MultiTarget.ModernRouteWorker"),
                        ("HostA", "net8.0", "SharedHostKey", "Issue34.HostA.Shared.LinkedInvoiceWorker"),
                        ("HostB", "net10.0", "SharedHostKey", "Issue34.HostB.Shared.SharedDispatchWorker") })
                    {
                        var module = Module(name, tfm); var binding = Bind(key, Catalog(module));
                        Check(name + "/" + tfm + " binding", binding.State == BindingState.Determined && binding.Candidates.Single().ClrName == clr, binding);
                    }
                    foreach (var (tfm, positive, absent) in new[] { ("net8.0", "Net8OnlyKey", "Net10OnlyKey"), ("net10.0", "Net10OnlyKey", "Net8OnlyKey") })
                    {
                        var catalog = Catalog(Module("MultiTarget", tfm));
                        Check("TFM isolation " + tfm, Bind(positive, catalog).State == BindingState.Determined && Bind(absent, catalog).State == BindingState.Unresolved,
                            new { positive = Bind(positive, catalog), absent = Bind(absent, catalog) });
                    }
                    var eightRoots = scripts.Roots(Module("MultiTarget", "net8.0")).Select(ScriptDocuments.Id).ToArray();
                    var tenRoots = scripts.Roots(Module("MultiTarget", "net10.0")).Select(ScriptDocuments.Id).ToArray();
                    Check("conditional None project item exists only in actual net8 Roots", eightRoots.Count(path => path.EndsWith("/MultiTarget/net8-only.fsm", StringComparison.OrdinalIgnoreCase)) == 1
                        && !tenRoots.Any(path => path.EndsWith("/MultiTarget/net8-only.fsm", StringComparison.OrdinalIgnoreCase)), new { eightRoots, tenRoots });
                    var exclusiveItems = ((IProject)Module("MultiTarget", "net8.0").ContainingProjectModule).GetAllProjectFiles(file => file.Name == "net8-only.fsm").ToArray();
                    Check("host has one evaluated conditional project item", exclusiveItems.Length == 1, exclusiveItems.Select(item => item.Location.FullPath).ToArray());
                    if (exclusiveItems.Length == 1)
                    {
                        var eightAction = exclusiveItems[0].Properties.GetBuildAction(Module("MultiTarget", "net8.0").TargetFrameworkId);
                        var tenAction = exclusiveItems[0].Properties.GetBuildAction(Module("MultiTarget", "net10.0").TargetFrameworkId);
                        Check("host evaluated build action is undefined only for net10", !eightAction.Equals(BuildAction.UNDEFINED) && tenAction.Equals(BuildAction.UNDEFINED),
                            new { eightAction = eightAction.ToString(), tenAction = tenAction.ToString() });
                    }
                    var shared = new List<IPsiSourceFile>();
                    foreach (var module in new[] { a, b })
                    {
                        var roots = scripts.Roots(module);
                        var linked = roots.Where(file => ScriptDocuments.Id(file).EndsWith("/SharedComplex/dual-host.fsm", StringComparison.OrdinalIgnoreCase)).ToArray();
                        Check("linked/projitems physical root " + Name(module), linked.Length == 1, roots.Select(ScriptDocuments.Id).ToArray());
                        shared.AddRange(linked);
                        var graph = scripts.Graph(module, epoch, out var sources);
                        Check("graph includes physical shared root " + Name(module), linked.Length == 1 && sources.ContainsKey(ScriptDocuments.Id(linked[0])), sources.Keys.ToArray());
                    }
                    Check("one physical script retains two consuming modules", shared.Count == 2 && ScriptDocuments.Id(shared[0]) == ScriptDocuments.Id(shared[1])
                        && shared[0].PsiModule.GetPersistentID() != shared[1].PsiModule.GetPersistentID(), shared.Select(file => new { path = ScriptDocuments.Id(file), host = file.PsiModule.GetPersistentID() }));
                    CheckOverrides(psi, a, Catalog(a), left, settings, scripts);
                    if (epoch != index.Epoch) { Retry("Epoch changed during SDK checks; abandoned evidence retained separately"); return; }
                    CheckMarker();
                    // The asynchronous public bridge seam is SDK evidence only, never a user action.
                    var sharedSource = Snapshot(shared.First());
                    var multi = Snapshot(scripts.Roots(Module("MultiTarget", "net8.0")).Single(file => ScriptDocuments.Id(file).EndsWith("/MultiTarget/framework.fsm", StringComparison.OrdinalIgnoreCase)));
                    running = true; timer?.Dispose();
                    _ = BridgeEvidence(lifetime, bridge, index, epoch, sharedSource, multi);
                }
                catch (OperationCanceledException error) { Retry("SDK host read cancelled; no zero-result pass: " + error.GetType().Name); }
                catch (Exception error) { failed = true; observations.Add(new { error = error.ToString() }); Report("failed"); timer?.Dispose(); }
                finally { Interlocked.Exchange(ref queued, 0); }
            });
        }, null, 1000, 1000);
        lifetime.OnTermination(() => {
            timer?.Dispose();
            if (detected && !completed) { waiting = new[] { "Solution lifetime ended before SDK evidence completed" }; Report(failed ? "failed" : "pendingVerification"); }
        });
    }

    private void CheckOverrides(IPsiServices psi, IPsiModule consumer, IReadOnlyList<NodeType> catalog, TypeIdentity left,
        BindingSettings settings, ScriptDocuments scripts)
    {
        var project = BindingSettings.ProjectPath(consumer)!;
        var script = scripts.Roots(consumer).Single(file => ScriptDocuments.Id(file).EndsWith("/HostA/sources.fsm", StringComparison.OrdinalIgnoreCase));
        var baseline = settings.Read(consumer, ScriptDocuments.Id(script));
        observations.Add(new { name = "actual adjacent BindingSettings.Read", configuration = baseline });
        var projectNodes = catalog.Single(type => type.Key == "ProjectLookupKey").Identity;
        var projectPath = BindingSettings.ProjectPath(TypeCatalog.Resolve(psi, projectNodes)!.Module)!;
        TypeTarget Target(string clr, params TypeTarget[] arguments) => new(projectPath, null, clr, "global", arguments);
        var replacement = Target("Issue34.ProjectLibrary.Warehouse+ShelfWorker");
        BindingOptions Options(params ConfiguredMapping[] mappings) => TypeCatalog.Options(consumer,
            new(KeyComparison.OrdinalIgnoreCase, mappings, Array.Empty<ModuleFile>()));
        ConfiguredMapping Map(TypeTarget type) => new("CurrentHostKey", type, "controlled SDK override (not disk/UI configuration)");
        var overridden = Bind("CurrentHostKey", catalog, Options(Map(replacement)));
        Check("explicit mapping replaces attribute convention", overridden.State == BindingState.Determined
            && overridden.Candidates.Single().ClrName == replacement.ClrName && overridden.Reason.Contains("Explicit mapping"), overridden);
        var invalid = Bind("CurrentHostKey", catalog, Options(Map(Target("Issue34.ProjectLibrary.DoesNotExist"))));
        Check("invalid explicit target never falls back to attribute", invalid.State == BindingState.Unresolved && invalid.Candidates.Count == 0, invalid);
        var duplicate = Bind("CurrentHostKey", catalog, Options(Map(replacement), Map(replacement)));
        Check("duplicate identical explicit entries remain ambiguous", duplicate.State == BindingState.Ambiguous, duplicate);
        var argument = new TypeTarget(project, null, "Issue34.HostA.InvoiceWorker", "global", Array.Empty<TypeTarget>());
        foreach (var clr in new[] { "Issue34.ProjectLibrary.AbstractWorker", "Issue34.ProjectLibrary.AbstractIntBufferWorker", "Issue34.ProjectLibrary.OrdinaryRecord" })
            Check("explicit non-concrete/non-node target refused: " + clr, Bind("CurrentHostKey", catalog, Options(Map(Target(clr)))).State == BindingState.Unresolved, clr);
        var genericClr = catalog.Single(type => type.Key == "OpenGenericKey").Identity.ClrName;
        var nestedClr = catalog.Single(type => type.Key == "OpenOuterKey").Identity.ClrName;
        foreach (var clr in new[] { genericClr, nestedClr })
        {
            var target = Target(clr, argument); var binding = Bind("CurrentHostKey", catalog, Options(Map(target)));
            Check("explicit closed generic incl. nested outer: " + clr, binding.State == BindingState.Determined
                && binding.Candidates.Single().Construction.Length > 0 && TypeCatalog.Resolve(psi, binding.Candidates.Single()) != null, binding);
            var other = Bind("CurrentHostKey", catalog, Options(Map(Target(clr, replacement))));
            Check("generic construction participates in identity: " + clr, other.State == BindingState.Determined
                && binding.State == BindingState.Determined && other.Candidates.Single() != binding.Candidates.Single(), new { binding, other });
            Check("unconstructed generic mapping rejected: " + clr,
                Bind("CurrentHostKey", catalog, Options(Map(Target(clr)))).State == BindingState.Unresolved, clr);
            Check("generic construction wrong arity rejected: " + clr,
                Bind("CurrentHostKey", catalog, Options(Map(Target(clr, argument, replacement)))).State == BindingState.Unresolved, clr);
        }
        var external = TypeCatalog.Resolve(psi, left)!;
        var aliasTarget = new TypeTarget(null, ((IAssemblyPsiModule)external.Module).Assembly.AssemblyName.FullName,
            left.ClrName, "LeftAssembly", Array.Empty<TypeTarget>());
        var aliasBinding = Bind("CurrentHostKey", catalog, Options(Map(aliasTarget)));
        Check("explicit assembly identity + real extern alias", aliasBinding.State == BindingState.Determined
            && aliasBinding.Candidates.Single().Module == left.Module && aliasBinding.Candidates.Single().Alias == "LeftAssembly", aliasBinding);
        var badAlias = Bind("CurrentHostKey", catalog, Options(Map(aliasTarget with { Alias = "NotAReferenceAlias" })));
        Check("nonexistent extern alias rejected without fallback", badAlias.State == BindingState.Unresolved, badAlias);
        var source = script.Document.GetText(); var parsed = FsmAnalysis.Analyze(new(ScriptDocuments.Id(script), 1, source));
        if (overridden.State == BindingState.Determined)
        {
            var references = parsed.FindTypeReferences(catalog, overridden.Candidates.Single(), Options(Map(replacement)));
            Check("explicit mapping participates in reverse key lookup", references.Any(reference => source.Substring(reference.Span.Start, reference.Span.Length) == "CurrentHostKey"), references);
            var old = catalog.Single(type => type.Key == "CurrentHostKey").Identity;
            Check("overridden convention removed from reverse key lookup", parsed.FindTypeReferences(catalog, old, Options(Map(replacement))).Count == 0, old);
        }
        var configurationPath = Path.Combine(Path.GetDirectoryName(project)!, "anotherfsm.json");
        var json = JsonSerializer.Serialize(new { version = 1, contexts = new[] { new { host = "HostA.csproj", targetFramework = "net8.0",
            scripts = new[] { "sources.fsm" }, mappings = new[] { new { key = "CurrentHostKey", type = new { project = "../ProjectNodes/ProjectNodes.csproj", clrName = replacement.ClrName } } } } } });
        var parsedConfiguration = BindingConfiguration.Parse(configurationPath, json);
        var selected = parsedConfiguration.Select(project, "net8.0", ScriptDocuments.Id(script));
        Check("JSON paths and exact script/TFM select real SDK target", Bind("CurrentHostKey", catalog, TypeCatalog.Options(consumer, selected)).State == BindingState.Determined
            && selected.Mappings.Count == 1 && selected.Mappings[0].Type.Project == projectPath, selected);
        Check("JSON configuration does not leak across TFM", parsedConfiguration.Select(project, "net10.0", ScriptDocuments.Id(script)).Mappings.Count == 0, "net10.0");
        Check("JSON configuration does not leak across scripts", parsedConfiguration.Select(project, "net8.0", ScriptDocuments.Id(script) + ".other").Mappings.Count == 0, "different physical path");
        var malformed = BindingConfiguration.Parse(configurationPath, "{").Select(project, "net8.0", ScriptDocuments.Id(script));
        Check("malformed JSON refuses convention", Bind("CurrentHostKey", catalog, TypeCatalog.Options(consumer, malformed)).State == BindingState.Unresolved, malformed);
    }

    private async Task BridgeEvidence(Lifetime lifetime, BridgeQueries bridge, ScriptIndex index, long epoch,
        ScriptSnapshot shared, ScriptSnapshot multi)
    {
        try
        {
            async Task<JsonElement> Query(ScriptSnapshot file, string token, bool implementation, object? selected = null)
            {
                var text = file.Text;
                var request = new Dictionary<string, object?> { ["action"] = "resolve", ["path"] = file.Path,
                    ["text"] = text, ["offset"] = text.IndexOf(token, StringComparison.Ordinal), ["documentVersion"] = 1,
                    ["requestId"] = Guid.NewGuid().ToString(), ["implementation"] = implementation };
                if (selected is JsonElement choice)
                {
                    request["host"] = choice.GetProperty("hosts")[0].GetProperty("id").GetString();
                    request["session"] = choice.GetProperty("session").GetString(); request["contextVersion"] = choice.GetProperty("contextVersion").GetInt64();
                }
                var raw = await bridge.Query(lifetime, JsonSerializer.Serialize(request)).WaitAsync(TimeSpan.FromSeconds(15));
                using var document = JsonDocument.Parse(raw); var reply = document.RootElement.Clone();
                observations.Add(new { name = "public bridge SDK query; not UI", request, reply }); return reply;
            }
            foreach (var (file, key, hosts) in new[] { (shared, "SharedHostKey", new[] { "HostA.csproj", "HostB.csproj" }),
                (multi, "FrameworkBindingKey", new[] { "net8.0", "net10.0" }) })
            {
                var choices = await Query(file, key, true);
                if (choices.GetProperty("status").GetString() is "analyzing" or "stale") { waiting = new[] { "SDK bridge changed context during evidence collection; rerun required" }; Report("pendingVerification"); return; }
                var rows = choices.TryGetProperty("hosts", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                Check("bridge requires explicit per-operation host/TFM choice: " + key, choices.GetProperty("status").GetString() == "hosts"
                    && rows.Length == 2 && rows.Select(row => row.GetProperty("id").GetString()).Distinct().Count() == 2
                    && hosts.All(host => rows.Any(row => row.GetProperty("label").GetString()!.Contains(host))), choices);
                if (rows.Length == 2)
                {
                    var selected = await Query(file, key, true, choices);
                    Check("selected host resolves full type identity: " + key, selected.GetProperty("status").GetString() == "type"
                        && selected.GetProperty("target").GetProperty("consumer").GetString() == rows[0].GetProperty("id").GetString(), selected);
                    var again = await Query(file, key, true);
                    Check("choice does not persist into another operation: " + key, again.GetProperty("status").GetString() == "hosts", again);
                }
            }
            // Local declaration queries merge the identical physical source location, retaining host groups.
            var declaration = await Query(shared, "SharedEntry", false);
            var locations = declaration.TryGetProperty("locations", out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
            Check("same physical source de-duplicated with both host groups", declaration.GetProperty("status").GetString() == "dsl"
                && locations.Length == 1 && locations[0].GetProperty("hosts").GetArrayLength() == 2, declaration);
            CheckMarker();
            if (epoch != index.Epoch) { waiting = new[] { "Epoch changed after SDK checks; evidence is not a coherent final pass" }; Report("pendingVerification"); }
            else Report(failed ? "failed" : "passed");
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        { waiting = new[] { "SDK bridge evidence incomplete: " + error }; Report("pendingVerification"); }
        catch (Exception error) { failed = true; observations.Add(new { error = error.ToString() }); Report("failed"); }
    }

    private void FocusedReadiness(IPsiServices psi, IEnumerable<IPsiModule> modules)
    {
        var values = modules.ToArray();
        var caches = psi.Solution.GetComponent<IPsiCachesState>();
        var required = caseId == "source-matrix"
            ? new[] { ("MatrixLibrary", "netstandard2.0"), ("MatrixPlain", "net8.0"), ("MatrixPlain", "net10.0"),
                ("MatrixWeb", "net8.0"), ("MatrixWeb", "net10.0"), ("MatrixWasm", "net8.0"), ("MatrixWasm", "net10.0"),
                ("MatrixDependency", "netstandard2.0"), ("HostA", "net8.0") }
            : new[] { ("HostA", "net8.0"), ("ProjectNodes", "netstandard2.0") };
        waiting = required.Where(item => !values.Any(module => Name(module) == item.Item1 && BindingSettings.Framework(module) == item.Item2
                && module.SourceFiles.Any(file => file.IsValid() && file.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))))
            .Select(item => "Source module not ready: " + item.Item1 + "/" + item.Item2).ToArray();
        if (!psi.Files.AllDocumentsAreCommitted || !caches.IsInitialUpdateFinished.Value || !caches.IsPrimaryIdle.Value || waiting.Length > 0)
        { Waiting(); return; }
        // Readiness is independent of whether a factory key binds. Alias provenance is plain
        // host metadata for this case's explicit mapping input, not a substitute for UI assertions.
        var host = values.Single(module => Name(module) == "HostA" && BindingSettings.Framework(module) == "net8.0");
        var definitions = psi.Modules.GetModuleReferences(host, host.GetContextFromModule())
            .Where(reference => reference.Module is IAssemblyPsiModule)
            .SelectMany(reference => reference.ExternAliases.Where(alias => alias is "LeftAssembly" or "RightAssembly")
                .Select(alias => new { alias, assembly = ((IAssemblyPsiModule)reference.Module).Assembly.AssemblyName.FullName })).ToArray();
        observations.Add(new { name = "alias-definitions", definitions });
        observations.Add(new { name = "independent-focused-readiness", committed = true, initialUpdate = true, primaryIdle = true });
        Report("ready"); timer?.Dispose();
    }

    private sealed record ScriptSnapshot(string Path, string Text);
    private static ScriptSnapshot Snapshot(IPsiSourceFile file) => new(ScriptDocuments.Id(file), file.Document.GetText());

    private void FinderCheck(IPsiServices psi, ScriptDocuments scripts, ITypeElement type, string key, string[] requiredPaths)
    {
        // Real host Finder over an actual solution scope, not direct plugin callbacks or catalog counts.
        var consumer = new Collector();
        var domain = psi.SearchDomainFactory.CreateSearchDomain(type.Module.GetSolution(), false);
        psi.SingleThreadedFinder.FindReferences(type, domain, consumer, NullProgressIndicator.Create());
        var rawResults = consumer.Snapshot();
        if (key is "HostDllKey" or "LocalPackageKey")
            Check("ordinary C# results survive aggregation: " + key, rawResults.Any(result => result.GetType().FullName != "AnotherFSM.Rider.FsmKeyResult"),
                rawResults.Select(result => result.GetType().FullName).Distinct().ToArray());
        var rows = rawResults.Select(result => new FsmOccurrenceProvider().MakeOccurrence(result))
            .OfType<RangeOccurrence>().Select(occurrence => {
                var range = occurrence.GetDocumentRange(); var span = range.TextRange;
                var valid = range.IsValid() && ((IOccurrence)occurrence).IsValid && span.StartOffset >= 0 && span.EndOffset <= range.Document.GetTextLength();
                return new { file = ScriptDocuments.Id(occurrence.SourceFile), start = span.StartOffset, length = span.Length,
                    text = valid ? range.Document.GetText(span) : "<invalid>", valid };
            }).ToArray();
        Check("real SDK Finder reverse: " + key, rows.Length > 0 && rows.All(row => row.valid && row.text == key)
            && requiredPaths.All(path => rows.Any(row => row.file.EndsWith(path, StringComparison.OrdinalIgnoreCase)))
            && rows.Select(row => (row.file, row.start, row.length)).Distinct().Count() == rows.Length, rows);
        // The per-file restricted scope must not leak another host's matching key.
        var source = scripts.Roots(scripts.Modules().Single(module => Name(module) == "HostA"))
            .Single(file => ScriptDocuments.Id(file).EndsWith("/HostA/sources.fsm", StringComparison.OrdinalIgnoreCase));
        var restricted = new Collector();
        psi.SingleThreadedFinder.FindReferences(type, psi.SearchDomainFactory.CreateSearchDomain(source), restricted, NullProgressIndicator.Create());
        var restrictedRows = restricted.Snapshot().Select(result => new FsmOccurrenceProvider().MakeOccurrence(result)).OfType<RangeOccurrence>().ToArray();
        Check("real SDK Finder restricted HostA source: " + key, restrictedRows.Length > 0 && restrictedRows.All(row =>
            ((IOccurrence)row).IsValid && ScriptDocuments.Id(row.SourceFile) == ScriptDocuments.Id(source) && row.GetDocumentRange().GetText() == key),
            restrictedRows.Select(row => new { file = ScriptDocuments.Id(row.SourceFile), range = row.GetTextRange().ToString() }).ToArray());
    }
    private sealed class Collector : IFindResultConsumer<FindResult>
    {
        private readonly List<FindResult> results = new();
        public FindResult Build(FindResult result) => result;
        public FindExecution Merge(FindResult result) { lock (results) results.Add(result); return FindExecution.Continue; }
        public FindResult[] Snapshot() { lock (results) return results.ToArray(); }
    }

    private static string Name(IPsiModule module) => (module.ContainingProjectModule as IProject)?.Name ?? "";
    private static NodeBinding Bind(string key, IEnumerable<NodeType> catalog, BindingOptions? options = null)
        => NodeBindings.Resolve(new("Probe", key, new(0, 5), new(6, key.Length), false), catalog, options);
    private void Check(string name, bool passed, object detail) { failed |= !passed; checks.Add(new { name, status = passed ? "passed" : "failed", detail }); Report("running"); }
    private void CheckMarker()
    {
        var marker = Environment.GetEnvironmentVariable("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");
        if (!string.IsNullOrEmpty(marker) && !Path.IsPathFullyQualified(marker)) throw new InvalidOperationException("Complex execution marker must be absolute: " + marker);
        if (!string.IsNullOrEmpty(marker) && File.Exists(marker)) throw new InvalidOperationException("Instrumented business execution marker exists; never deleted: " + marker + "\n" + File.ReadAllText(marker));
    }
    private void Retry(string reason)
    {
        if (checks.Count > 0 || observations.Count > 0)
            abandonedAttempts.Add(new { status = "abandoned", reason, checks = checks.ToArray(), observations = observations.ToArray() });
        checks.Clear(); observations.Clear(); failed = false; stableEpoch = -1; waiting = new[] { reason }; Waiting();
    }
    private void Waiting()
    {
        if (DateTime.UtcNow - started > TimeSpan.FromMinutes(3)) { Report("pendingVerification"); timer?.Dispose(); }
        else Report("waiting");
    }
    private void Report(string status)
    {
        if (!detected || directory == null) return;
        if (status is "passed" or "failed" or "pendingVerification" or "ready") completed = true;
        try
        {
            Directory.CreateDirectory(directory);
            var marker = Environment.GetEnvironmentVariable("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");
            File.WriteAllText(Path.Combine(directory, "complex-sdk-probe.json"), JsonSerializer.Serialize(new {
                status, @case = caseId, scope = caseId == "all" ? "full SDK checks" : "readiness only, not full SDK checks",
                seam = "target SDK/public core queries + real BridgeQueries; not UI acceptance", backendPid = Environment.ProcessId,
                elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds, waiting, checks, observations, abandonedAttempts,
                sentinel = new { path = marker, configured = !string.IsNullOrEmpty(marker), exists = !string.IsNullOrEmpty(marker) && File.Exists(marker), claim = "instrumented paths only; absence is not universal execution/network safety proof" },
                notClaimed = new[] { "real UI actions/native C# Find Usages (separate journey)", "live configuration buffer/project/reference reload invalidation",
                    "verified external source, Source Link/offline/authorization diagnosis", "WebAssembly execution/AOT/publish (only real SDK static analysis)", "#35 completion", "#36 performance" }
            }, Json));
        }
        catch (Exception error) { Console.Error.WriteLine("ComplexSdkProbe report failure: " + error); }
    }
}
