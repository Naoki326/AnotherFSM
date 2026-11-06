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
    private bool closed;
    public Guid Session { get; } = Guid.NewGuid();
    public long Epoch => Interlocked.Read(ref epoch);
    public event Action? Invalidated;

    public ScriptIndex(Lifetime lifetime, IPsiServices psi, ChangeManager changes)
    {
        changes.Changed.Advise(lifetime, args => {
            var structural = args.ChangeMap.GetChanges<PsiModuleChange>().Any()
                || args.ChangeMap.GetChanges<ProjectModelChange>().Any();
            if (structural || args.ChangeMap.GetChanges<JetBrains.DocumentModel.DocumentChange>().Any()) Invalidate(structural, structural);
        });
        psi.Files.AfterPsiChanged += OnPsiChanged;
        lifetime.OnTermination(() => {
            psi.Files.AfterPsiChanged -= OnPsiChanged;
            lock (gate) { closed = true; documents.Dispose(); catalogs.Clear(); }
            Invalidate(true);
            Invalidated = null;
        });
    }
    private void OnPsiChanged(JetBrains.ReSharper.Psi.Tree.ITreeNode? node, PsiChangedElementType kind)
    {
        // A null node denotes a broad PSI change, e.g. initial solution commit.
        if (node == null || node.GetSourceFile()?.PrimaryPsiLanguage is CSharpLanguage) Invalidate(true);
    }
    private void Invalidate(bool typesChanged, bool documentSetChanged = false)
    {
        lock (gate)
        {
            if (typesChanged) { catalogs.Clear(); typeVersion++; }
            Interlocked.Increment(ref epoch);
            if (documentSetChanged && !closed)
            {
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
    {
        if (expectedEpoch.HasValue && expectedEpoch.Value != Epoch) throw new OperationCanceledException("AnotherFSM query context expired");
        var key = file.GetLocation().FullPath + "|" + file.PsiModule.GetPersistentID();
        var text = file.Document.GetText();
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
        if (result == null || closed || file.Document.GetText() != text)
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
