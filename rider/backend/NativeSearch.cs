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
    // 'def' also finds implicit factory keys, which need not resemble the CLR class name.
    public override IEnumerable<string> GetAllPossibleWordsInFile(IDeclaredElement element)
        => IsTarget(element) ? new[] { "def" } : Array.Empty<string>();
    public override ISearchDomain GetDeclaredElementSearchDomain(IDeclaredElement element)
        => IsTarget(element) && element is ITypeElement type
            ? domains.CreateSearchDomain(type.Module.SourceFiles.Where(ScriptIndex.IsScript).ToArray()) : EmptySearchDomain.Instance;
    public override IDomainSpecificSearcher CreateReferenceSearcher(IDeclaredElementsSet elements, ReferenceSearcherParameters parameters)
    {
        var types = elements.Where(IsTarget).OfType<ITypeElement>().ToArray();
        if (types.Length == 0) return null!;
        var index = types[0].Module.GetSolution().GetComponent<ScriptIndex>();
        if (types.Any(type => !ReferenceEquals(index, type.Module.GetSolution().GetComponent<ScriptIndex>()))) return null!;
        return new FsmSearcher(types.Select(TypeCatalog.Identity).ToArray(), index, index.Epoch);
    }
}

internal sealed class FsmSearcher(TypeIdentity[] targets, ScriptIndex index, long epoch) : IDomainSpecificSearcher
{
    public bool ProcessProjectItem<T>(IPsiSourceFile file, IFindResultConsumer<T> consumer)
    {
        if (!ScriptIndex.IsScript(file)) return false;
        if (!ReferenceEquals(index, file.PsiModule.GetSolution().GetComponent<ScriptIndex>())) return false;
        if (epoch != index.Epoch) throw new OperationCanceledException("AnotherFSM query context expired");
        var analysis = index.Read(file, epoch);
        var catalog = index.Catalog(file.PsiModule);
        foreach (var target in targets.Where(t => t.Module == file.PsiModule.GetPersistentID()
            && t.Context == file.PsiModule.TargetFrameworkId.UniqueString))
        {
            foreach (var reference in analysis.FindTypeReferences(catalog, target))
            {
                JetBrains.Application.Interruption.Current.CheckAndThrow();
                if (epoch != index.Epoch || file.Document.GetText() != analysis.Document.Text)
                    throw new OperationCanceledException("AnotherFSM search snapshot expired; refresh Find Usages");
                if (consumer.Accept(new FsmKeyResult(file, index, epoch, analysis.Document.Text, reference, target)) == FindExecution.Stop)
                    return true;
            }
        }
        return false;
    }
    public bool ProcessElement<T>(ITreeNode element, IFindResultConsumer<T> consumer)
        => element.GetSourceFile() is { } file && ProcessProjectItem(file, consumer);
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
