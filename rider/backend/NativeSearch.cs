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
        var index = type.Module.GetSolution().GetComponent<ScriptIndex>();
        return index.Catalog(type.Module).Any(t => t.Identity == TypeCatalog.Identity(type));
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
        scripts.Graph(type.Module, index.Epoch, out var sources);
        return domains.CreateSearchDomain(sources.Values.ToArray());
    }
    public override IDomainSpecificSearcher CreateReferenceSearcher(IDeclaredElementsSet elements, ReferenceSearcherParameters parameters)
    {
        var types = elements.Where(IsTarget).OfType<ITypeElement>().ToArray();
        if (types.Length == 0) return null!;
        var index = types[0].Module.GetSolution().GetComponent<ScriptIndex>();
        if (types.Any(type => !ReferenceEquals(index, type.Module.GetSolution().GetComponent<ScriptIndex>()))) return null!;
        return new FsmSearcher(types.Select(TypeCatalog.Identity).ToArray(), index,
            types[0].Module.GetSolution().GetComponent<ScriptDocuments>(), index.Epoch);
    }
    public override IEnumerable<ITwoPhaseDomainSpecificSearcher> CreateImplicitUsageSearchers(SearchPattern pattern, ICollection<IDeclaredElement> elements)
    {
        var types = elements.Where(IsTarget).OfType<ITypeElement>().ToArray();
        if (types.Length == 0) yield break;
        var solution = types[0].Module.GetSolution();
        if (types.Any(type => !ReferenceEquals(solution, type.Module.GetSolution()))) yield break;
        var index = solution.GetComponent<ScriptIndex>();
        // Created before the named visitor starts. Both phases retain this epoch, including resumable callbacks.
        yield return new FsmImportSearcher(types.Select(TypeCatalog.Identity).ToArray(), index,
            solution.GetComponent<ScriptDocuments>(), index.Epoch);
    }
}

/// <summary>SDK-supported second-phase discovery for semantic import sources absent from module.SourceFiles.
/// It retains the original user scope; no source is submitted through another file's callback.</summary>
internal sealed class FsmImportSearcher(TypeIdentity[] targets, ScriptIndex index, ScriptDocuments scripts, long epoch) : ITwoPhaseDomainSpecificSearcher
{
    private void Check()
    {
        JetBrains.Application.Interruption.Current.CheckAndThrow();
        if (index.Epoch != epoch) throw new OperationCanceledException("AnotherFSM import search context expired");
    }
    public bool ProcessProjectItem<T>(IPsiSourceFile file, IFindResultConsumer<T> consumer) { Check(); return false; }
    public bool ProcessElement<T>(ITreeNode element, IFindResultConsumer<T> consumer) { Check(); return false; }
    public (IDomainSpecificSearcher? Searcher, ISearchDomain? SearchDomain) TryCreateSecondPhaseInfo(
        SearchDomainFactory domains, ISearchDomain originalSearchDomain, HashSet<IPsiSourceFile>? processedFiles)
    {
        Check();
        var additional = new List<IPsiSourceFile>();
        foreach (var module in scripts.Modules().Where(module => targets.Any(target => target.Module == module.GetPersistentID()
            && target.Context == module.TargetFrameworkId.UniqueString)))
        {
            Check();
            var roots = scripts.Roots(module).Select(ScriptDocuments.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            scripts.Graph(module, epoch, out var sources);
            additional.AddRange(sources.Values.Where(file => !roots.Contains(ScriptDocuments.Id(file))
                && (processedFiles == null || !processedFiles.Any(processed => processed.PsiModule.GetPersistentID() == module.GetPersistentID()
                    && StringComparer.OrdinalIgnoreCase.Equals(ScriptDocuments.Id(processed), ScriptDocuments.Id(file))))));
        }
        Check();
        // A broad module domain can enumerate only its own SourceFiles even though algebraically it
        // contains a known import source. The explicit, intersected second phase preserves that source.
        var domain = domains.CreateSearchDomain(additional).Intersect(originalSearchDomain);
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
            foreach (var target in targets.Where(t => t.Module == file.PsiModule.GetPersistentID()
                && t.Context == file.PsiModule.TargetFrameworkId.UniqueString))
            {
                foreach (var reference in analysis.FindTypeReferences(catalog, target))
                {
                    JetBrains.Application.Interruption.Current.CheckAndThrow();
                    if (epoch != index.Epoch || file.Document.GetText() != analysis.Document.Text)
                        throw new OperationCanceledException("AnotherFSM search snapshot expired; refresh Find Usages");
                    if (restrictedRange is { } range && (reference.Span.Start < range.StartOffset
                        || reference.Span.Start + reference.Span.Length > range.EndOffset)) continue;
                    if (graph.FindNodeSource(analysis.Document.Id, reference.Span.Start) == null) continue;
                    if (consumer.Accept(new FsmKeyResult(file, index, epoch, analysis.Document.Text, reference, target)) == FindExecution.Stop)
                        return true;
                }
            }
        }
        return false;
    }
    public bool ProcessElement<T>(ITreeNode element, IFindResultConsumer<T> consumer)
    {
        var range = element.GetDocumentRange();
        return element.GetSourceFile() is { } file && range.IsValid() && Process(file, range.TextRange, consumer);
    }
}

internal sealed class FsmKeyResult(IPsiSourceFile file, ScriptIndex index, long epoch, string snapshot, TypeReference reference, TypeIdentity identity)
    : FindResult, IFindResultWithTarget
{
    public IPsiSourceFile File { get; } = file;
    public ScriptIndex Index { get; } = index;
    public long Generation => epoch;
    public TypeReference Reference { get; } = reference;
    public bool Current => epoch == Index.Epoch && File.IsValid() && File.PsiModule.IsValid()
        && File.Document.GetText() == snapshot && TypeCatalog.Resolve(File.GetPsiServices(), identity) != null;
    public IDeclaredElement Target => Current ? TypeCatalog.Resolve(File.GetPsiServices(), identity)! : null!;
    public DocumentRange Range => new(File.Document, new TextRange(Reference.Span.Start, Reference.Span.Start + Reference.Span.Length));
    public override bool Equals(object? obj) => obj is FsmKeyResult other && Equals(File, other.File)
        && epoch == other.epochValue && Reference == other.Reference && identity == other.identityValue;
    private long epochValue => epoch;
    private TypeIdentity identityValue => identity;
    public override int GetHashCode() => HashCode.Combine(File, epoch, Reference, identity);
}
