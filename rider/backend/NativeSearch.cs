using AnotherFSM.Semantics;
using JetBrains.DocumentModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.ExtensionsAPI;
using JetBrains.ReSharper.Psi.Impl.Search.SearchDomain;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;

namespace AnotherFSM.Rider;

[PsiSharedComponent]
public sealed class FsmSearcherFactory(SearchDomainFactory domains) : DomainSpecificSearcherFactoryBase
{
    public override bool IsCompatibleWithLanguage(PsiLanguageType language) => true;
    internal static bool IsTarget(IDeclaredElement element)
    {
        if (element is not ITypeElement type || !type.IsValid()) return false;
        var solution = type.Module.GetSolution();
        var index = solution.GetComponent<ScriptIndex>();
        var scripts = solution.GetComponent<ScriptDocuments>();
        return ConsumerModules(new[] { TypeCatalog.Identity(type) }, index, scripts).Any();
    }
    internal static IEnumerable<JetBrains.ReSharper.Psi.Modules.IPsiModule> ConsumerModules(TypeIdentity[] targets, ScriptIndex index, ScriptDocuments scripts)
    {
        foreach (var module in scripts.Modules())
        {
            JetBrains.Application.Interruption.Current.CheckAndThrow();
            if (index.Catalog(module).Any(type => targets.Any(target => TypeCatalog.SameDefinition(type.Identity, target))))
            { yield return module; continue; }
            var settings = module.GetSolution().GetComponent<BindingSettings>();
            var graph = scripts.Graph(module, index.Epoch, out _);
            if (graph.Documents.Any(document => TypeCatalog.Options(module, settings.Read(module, document.Document.Id)).Mappings
                .SelectMany(mapping => mapping.Targets).Any(type => targets.Any(target => TypeCatalog.SameDefinition(type.Identity, target))))) yield return module;
        }
    }
    // A root may only import/use a template; its real factory keys then live in the dependency.
    // None of these candidate words is a semantic reference by itself.
    public override IEnumerable<string> GetAllPossibleWordsInFile(IDeclaredElement element)
        => IsTarget(element) ? new[] { "def", "import", "use" } : Array.Empty<string>();
    public override ISearchDomain GetDeclaredElementSearchDomain(IDeclaredElement element)
    {
        if (!IsTarget(element) || element is not ITypeElement type) return EmptySearchDomain.Instance;
        var solution = type.Module.GetSolution();
        var index = solution.GetComponent<ScriptIndex>();
        var scripts = solution.GetComponent<ScriptDocuments>();
        // Include import-only sources in the declared domain so Rider can intersect it with the user's
        // actual query scope. A callback must never submit another file that bypassed that intersection.
        var files = new List<IPsiSourceFile>();
        foreach (var module in ConsumerModules(new[] { TypeCatalog.Identity(type) }, index, scripts))
        { scripts.Graph(module, index.Epoch, out var sources); files.AddRange(sources.Values); }
        return domains.CreateSearchDomain(files.ToArray());
    }
    public override IDomainSpecificSearcher CreateReferenceSearcher(IDeclaredElementsSet elements, ReferenceSearcherParameters parameters)
    {
        var types = elements.Where(IsTarget).OfType<ITypeElement>().ToArray();
        if (types.Length == 0) return null!;
        var index = types[0].Module.GetSolution().GetComponent<ScriptIndex>();
        if (types.Any(type => !ReferenceEquals(index, type.Module.GetSolution().GetComponent<ScriptIndex>()))) return null!;
        return new FsmSearcher(types.Select(type => TypeCatalog.Identity(type)).ToArray(), index,
            types[0].Module.GetSolution().GetComponent<ScriptDocuments>(), index.Epoch);
    }
    public override IEnumerable<ITwoPhaseDomainSpecificSearcher> CreateImplicitUsageSearchers(SearchPattern pattern, ICollection<IDeclaredElement> elements)
    {
        var types = elements.Where(IsTarget).OfType<ITypeElement>().ToArray();
        if (types.Length == 0) yield break;
        var solution = types[0].Module.GetSolution();
        if (types.Any(type => !ReferenceEquals(solution, type.Module.GetSolution()))) yield break;
        var index = solution.GetComponent<ScriptIndex>();
        var epoch = index.Epoch;
        JetBrains.Application.Interruption.Current.CheckAndThrow();
        var identities = types.Select(type => TypeCatalog.Identity(type)).ToArray();
        if (index.Epoch != epoch) throw new OperationCanceledException("AnotherFSM import search context expired");
        // Created before the named visitor starts. Both phases retain this epoch, including resumable callbacks.
        yield return new FsmImportSearcher(identities, index, solution.GetComponent<ScriptDocuments>(), epoch);
    }
}

/// <summary>SDK-supported second-phase discovery for semantic import sources absent from module.SourceFiles.
/// It retains the original user scope; no source is submitted through another file's callback.</summary>
internal sealed class FsmImportSearcher(TypeIdentity[] targets, ScriptIndex index, ScriptDocuments scripts, long epoch) : ITwoPhaseDomainSpecificSearcher
{
    private readonly HashSet<string> completed = new(StringComparer.OrdinalIgnoreCase);
    private readonly FsmSearcher firstPhase = new(targets, index, scripts, epoch);
    private static string FileKey(IPsiSourceFile file) => file.PsiModule.GetPersistentID() + "|" + ScriptDocuments.Id(file);
    private void Check()
    {
        JetBrains.Application.Interruption.Current.CheckAndThrow();
        if (index.Epoch != epoch) throw new OperationCanceledException("AnotherFSM import search context expired");
    }
    private bool IsImportOnly(IPsiSourceFile file) => ScriptIndex.IsScript(file)
        && !scripts.Roots(file.PsiModule).Any(root => StringComparer.OrdinalIgnoreCase.Equals(ScriptDocuments.Id(root), ScriptDocuments.Id(file)));
    public bool ProcessProjectItem<T>(IPsiSourceFile file, IFindResultConsumer<T> consumer)
    {
        Check();
        if (!IsImportOnly(file)) return false;
        var stopped = firstPhase.ProcessProjectItem(file, consumer);
        Check();
        if (!stopped) lock (completed) completed.Add(FileKey(file));
        return stopped;
    }
    public bool ProcessElement<T>(ITreeNode element, IFindResultConsumer<T> consumer)
    {
        Check();
        // A node visit is not proof that the whole source has been searched.
        return element.GetSourceFile() is { } file && IsImportOnly(file) && firstPhase.ProcessElement(element, consumer);
    }
    public (IDomainSpecificSearcher? Searcher, ISearchDomain? SearchDomain) TryCreateSecondPhaseInfo(
        SearchDomainFactory domains, ISearchDomain originalSearchDomain, HashSet<IPsiSourceFile>? processedFiles)
    {
        Check();
        var additional = new List<IPsiSourceFile>();
        HashSet<string> searched;
        lock (completed) searched = new(completed, StringComparer.OrdinalIgnoreCase);
        // SDK processedFiles also includes sources visited only through individual nodes; it cannot
        // prove full coverage. Only this adapter's completed whole-file callbacks suppress phase two.
        foreach (var module in FsmSearcherFactory.ConsumerModules(targets, index, scripts))
        {
            Check();
            var roots = scripts.Roots(module).Select(ScriptDocuments.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            scripts.Graph(module, epoch, out var sources);
            additional.AddRange(sources.Values.Where(file => !roots.Contains(ScriptDocuments.Id(file)) && !searched.Contains(FileKey(file))));
        }
        Check();
        // A broad module domain can enumerate only its own SourceFiles even though algebraically it
        // contains a known import source. The explicit, intersected second phase preserves that source.
        var domain = domains.CreateSearchDomain(additional).Intersect(originalSearchDomain);
        Check();
        return domain.IsEmpty ? (null, null) : (new FsmSearcher(targets, index, scripts, epoch), domain);
    }
}

internal sealed class FsmSearcher(TypeIdentity[] targets, ScriptIndex index, ScriptDocuments scripts, long epoch) : IDomainSpecificSearcher
{
    public bool ProcessProjectItem<T>(IPsiSourceFile file, IFindResultConsumer<T> consumer) => Process(file, null, consumer);

    private bool Process<T>(IPsiSourceFile file, TextRange? restrictedRange, IFindResultConsumer<T> consumer)
    {
        if (!ScriptIndex.IsScript(file)) return false;
        if (!ReferenceEquals(index, file.PsiModule.GetSolution().GetComponent<ScriptIndex>())) return false;
        if (epoch != index.Epoch) throw new OperationCanceledException("AnotherFSM query context expired");
        // Only the source selected by Rider's search-domain visitor may contribute results.
        // Do not permanently mark a module/file visited: the host owns duplicate handling and resumable search.
        var graph = scripts.Graph(file.PsiModule, epoch, out _);
        var catalog = index.Catalog(file.PsiModule);
        foreach (var analysis in graph.Documents.Where(document => StringComparer.OrdinalIgnoreCase.Equals(document.Document.Id, ScriptDocuments.Id(file))))
        {
            var settings = file.PsiModule.GetSolution().GetComponent<BindingSettings>();
            var options = TypeCatalog.Options(file.PsiModule, settings.Read(file.PsiModule, analysis.Document.Id));
            var identities = catalog.Concat(options.Mappings.SelectMany(mapping => mapping.Targets)).Select(type => type.Identity)
                .Where(identity => targets.Any(target => TypeCatalog.SameDefinition(identity, target))).Distinct();
            foreach (var target in identities)
            {
                foreach (var reference in analysis.FindTypeReferences(catalog, target, options))
                {
                    JetBrains.Application.Interruption.Current.CheckAndThrow();
                    if (epoch != index.Epoch || file.Document.GetText() != analysis.Document.Text)
                        throw new OperationCanceledException("AnotherFSM search snapshot expired; refresh Find Usages");
                    if (restrictedRange is { } range && (reference.Span.Start < range.StartOffset
                        || reference.Span.Start + reference.Span.Length > range.EndOffset)) continue;
                    if (graph.FindNodeSource(analysis.Document.Id, reference.Span.Start) == null) continue;
                    var node = analysis.FindFactoryDeclaration(reference.Span.Start);
                    var reason = node == null ? "Binding source unavailable" : NodeBindings.Resolve(node, catalog, options).Reason;
                    if (consumer.Accept(new FsmKeyResult(file, index, epoch, analysis.Document.Text, reference, target, reason,
                        HostAssociations(file, reference, target))) == FindExecution.Stop)
                        return true;
                }
            }
        }
        return false;
    }
    private IReadOnlyList<string> HostAssociations(IPsiSourceFile file, TypeReference reference, TypeIdentity target)
    {
        var associations = new List<string>();
        foreach (var module in FsmSearcherFactory.ConsumerModules(targets, index, scripts))
        {
            var graph = scripts.Graph(module, epoch, out var sources);
            if (!sources.TryGetValue(ScriptDocuments.Id(file), out var source) || source.Document.GetText() != file.Document.GetText()) continue;
            var document = graph.Documents.Single(item => StringComparer.OrdinalIgnoreCase.Equals(item.Document.Id, ScriptDocuments.Id(file)));
            var settings = module.GetSolution().GetComponent<BindingSettings>();
            var options = TypeCatalog.Options(module, settings.Read(module, document.Document.Id));
            var node = document.FindFactoryDeclaration(reference.Span.Start);
            if (node == null) continue;
            var binding = NodeBindings.Resolve(node, index.Catalog(module), options);
            if (binding.State == BindingState.Determined && TypeCatalog.SameDefinition(binding.Candidates.Single(), target))
                associations.Add((BindingSettings.ProjectPath(module) ?? module.GetPersistentID()) + " / " + BindingSettings.Framework(module) + " — " + binding.Reason);
        }
        return associations.Distinct().Order().ToArray();
    }
    public bool ProcessElement<T>(ITreeNode element, IFindResultConsumer<T> consumer)
    {
        var range = element.GetDocumentRange();
        return element.GetSourceFile() is { } file && range.IsValid() && Process(file, range.TextRange, consumer);
    }
}

internal sealed class FsmKeyResult(IPsiSourceFile file, ScriptIndex index, long epoch, string snapshot, TypeReference reference, TypeIdentity identity,
    string bindingReason, IReadOnlyList<string> hosts)
    : FindResult, IFindResultWithTarget
{
    public IPsiSourceFile File { get; } = file;
    public ScriptIndex Index { get; } = index;
    public long Generation => epoch;
    public TypeReference Reference { get; } = reference;
    public string BindingReason { get; } = bindingReason;
    public IReadOnlyList<string> Hosts { get; } = hosts;
    public bool Current => epoch == Index.Epoch && File.IsValid() && File.PsiModule.IsValid()
        && File.Document.GetText() == snapshot && TypeCatalog.Resolve(File.GetPsiServices(), identity) != null;
    public IDeclaredElement Target => Current ? TypeCatalog.Resolve(File.GetPsiServices(), identity)! : null!;
    public DocumentRange Range => new(File.Document, new TextRange(Reference.Span.Start, Reference.Span.Start + Reference.Span.Length));
    public override bool Equals(object? obj) => obj is FsmKeyResult other
        && StringComparer.OrdinalIgnoreCase.Equals(ScriptDocuments.Id(File), ScriptDocuments.Id(other.File))
        && epoch == other.epochValue && Reference == other.Reference && TypeCatalog.SameDefinition(identity, other.identityValue);
    private long epochValue => epoch;
    private TypeIdentity identityValue => identity;
    public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(ScriptDocuments.Id(File)), epoch, Reference, identity.Module, identity.ClrName);
}
