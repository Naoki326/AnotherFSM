using AnotherFSM.Semantics;
using JetBrains.Application;
using JetBrains.Application.Parts;
using JetBrains.Application.changes;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Modules;

namespace AnotherFSM.Rider;

[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ScriptIndex
{
    private readonly object gate = new();
    private FsmWorkspace documents = new();
    private readonly Dictionary<string, IReadOnlyList<NodeType>> catalogs = new();
    private long epoch;
    private long version;
    private long typeVersion;
    private long structureVersion;
    public long StructureVersion => Interlocked.Read(ref structureVersion);
    private bool closed;
    public Guid Session { get; } = Guid.NewGuid();
    public long Epoch => Interlocked.Read(ref epoch);
    public event Action? Invalidated;

    public ScriptIndex(Lifetime lifetime, IPsiServices psi, ChangeManager changes)
    {
        changes.Changed.Advise(lifetime, args => {
            var structural = args.ChangeMap.GetChanges<PsiModuleChange>().Any()
                || args.ChangeMap.GetChanges<ProjectModelChange>().Any();
            var fileSystem = args.ChangeMap.GetChanges<FileSystemChange>().Any(change => change.Deltas.Any(ScriptChanged));
            if (structural || fileSystem || args.ChangeMap.GetChanges<JetBrains.DocumentModel.DocumentChange>().Any())
                Invalidate(structural, structural, fileSystem);
        });
        psi.Files.AfterPsiChanged += OnPsiChanged;
        lifetime.OnTermination(() => {
            psi.Files.AfterPsiChanged -= OnPsiChanged;
            lock (gate) { closed = true; documents.Dispose(); catalogs.Clear(); }
            Invalidate(true);
            Invalidated = null;
        });
    }
    private static bool ScriptChanged(FileSystemChangeDelta delta) =>
        (!delta.OldPath.IsEmpty && ScriptDocuments.IsScriptName(delta.OldPath.Name))
        || (!delta.NewPath.IsEmpty && ScriptDocuments.IsScriptName(delta.NewPath.Name))
        || delta.Children.Any(ScriptChanged);

    private void OnPsiChanged(JetBrains.ReSharper.Psi.Tree.ITreeNode? node, PsiChangedElementType kind)
    {
        // A null node denotes a broad PSI change, e.g. initial solution commit.
        if (node == null || node.GetSourceFile()?.PrimaryPsiLanguage is CSharpLanguage) Invalidate(true);
    }
    private void Invalidate(bool typesChanged, bool documentSetChanged = false, bool inventoryChanged = false)
    {
        lock (gate)
        {
            if (typesChanged) { catalogs.Clear(); typeVersion++; }
            Interlocked.Increment(ref epoch);
            if (inventoryChanged && !documentSetChanged) Interlocked.Increment(ref structureVersion);
            if (documentSetChanged && !closed)
            {
                Interlocked.Increment(ref structureVersion);
                documents.Dispose();
                documents = new FsmWorkspace();
            }
            else documents.InvalidateContext();
        }
        Invalidated?.Invoke();
    }
    public static bool IsScript(IPsiSourceFile file) => file.IsValid() &&
        (file.Name.EndsWith(".fsm", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".FSMScript", StringComparison.OrdinalIgnoreCase));
    public DocumentAnalysis Read(IPsiSourceFile file, long? expectedEpoch = null)
        => Read(ScriptDocuments.Id(file), file.Document, expectedEpoch);

    public DocumentAnalysis Read(string key, JetBrains.DocumentModel.IDocument document, long? expectedEpoch = null)
    {
        if (expectedEpoch.HasValue && expectedEpoch.Value != Epoch) throw new OperationCanceledException("AnotherFSM query context expired");
        // Syntax is independent of the consuming host. Graph/binding identities retain their own context.
        var text = document.GetText();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (documents.GetCurrent(key) is { } cached && cached.Analysis.Document.Text == text) return cached.Analysis;
        }
        var result = documents.Update(new(key, Interlocked.Increment(ref version), text),
            checkHostCancellation: () => {
                Interruption.Current.CheckAndThrow();
                if (expectedEpoch.HasValue && expectedEpoch.Value != Epoch) throw new OperationCanceledException("AnotherFSM query context expired");
            });
        if (result == null || closed || document.GetText() != text)
            throw new OperationCanceledException("Document changed during analysis");
        return result.Analysis;
    }
    public IReadOnlyList<NodeType> Catalog(IPsiModule module)
    {
        var key = module.GetPersistentID();
        long expectedVersion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (catalogs.TryGetValue(key, out var cached)) return cached;
            expectedVersion = typeVersion;
        }
        var current = TypeCatalog.Read(module);
        lock (gate)
        {
            if (closed || expectedVersion != typeVersion) throw new OperationCanceledException("AnotherFSM type context changed");
            if (catalogs.Count >= 128) catalogs.Clear();
            catalogs[key] = current;
        }
        return current;
    }
}
