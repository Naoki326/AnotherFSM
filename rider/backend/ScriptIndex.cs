using AnotherFSM.Semantics;
using JetBrains.Application;
using JetBrains.Application.Parts;
using JetBrains.Application.changes;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.impl;
using JetBrains.DocumentModel;

namespace AnotherFSM.Rider;

[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ScriptIndex
{
    private readonly object gate = new();
    private readonly IPsiServices psi;
    private string? inputSignature;
    private readonly HashSet<string> presentationDocuments = new(StringComparer.OrdinalIgnoreCase);
    private sealed record TrackedDocument(IDocument Document, string Stamp, string Text);
    private readonly Dictionary<string, TrackedDocument> tracked = new(StringComparer.OrdinalIgnoreCase);
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
        this.psi = psi;
        changes.Changed.Advise(lifetime, args => {
            var structural = args.ChangeMap.GetChanges<PsiModuleChange>().Any(change => change.AssemblyChanges.Count > 0
                || change.ModuleChanges.Count > 0 || change.FileChanges.Any(file => !IsPresentation(file.Item, file.Type)))
                || args.ChangeMap.GetChanges<ProjectModelChange>().Any(RelevantProjectChange);
            var fileSystem = args.ChangeMap.GetChanges<FileSystemChange>().Any(change => change.Deltas.Any(ScriptChanged));
            if (structural || fileSystem)
                Invalidate(structural, structural, fileSystem);
            else
            {
                var changed = args.ChangeMap.GetChanges<DocumentChange>().Select(change => change.Document).ToArray();
                if (changed.Length > 0) RefreshInputs(changed);
            }
        });
        psi.Files.AfterPsiChanged += OnPsiChanged;
        lifetime.OnTermination(() => {
            psi.Files.AfterPsiChanged -= OnPsiChanged;
            lock (gate) { closed = true; documents.Dispose(); catalogs.Clear(); tracked.Clear(); presentationDocuments.Clear(); }
            Invalidate(true);
            Invalidated = null;
        });
    }
    private static bool ScriptChanged(FileSystemChangeDelta delta) =>
        (!delta.OldPath.IsEmpty && IsRelevant(delta.OldPath.Name))
        || (!delta.NewPath.IsEmpty && IsRelevant(delta.NewPath.Name))
        || delta.Children.Any(ScriptChanged);

    private static bool IsRelevant(string name) => ScriptDocuments.IsScriptName(name)
        || StringComparer.OrdinalIgnoreCase.Equals(name, "anotherfsm.json");

    private void OnPsiChanged(JetBrains.ReSharper.Psi.Tree.ITreeNode? node, PsiChangedElementType kind)
    {
        var source = node?.GetSourceFile();
        if (source != null && IsPresentation(source, PsiModuleChange.ChangeType.Modified)) return;
        // Broad source PSI also describes SDK-generated external views. Check actual business inputs,
        // never the global PSI timestamp; real assembly/module deltas remain unconditional above.
        if (source?.PrimaryPsiLanguage is CSharpLanguage && source.PsiModule.ContainingProjectModule is IProject)
            Invalidate(true);
        else if (node == null)
        {
            // SDK metadata resolution can advance without a project-input change. Never retain
            // a partial symbol scope across that notification. This is cache freshness, not proof
            // that business bindings changed: generated external views must not expire UI results.
            lock (gate) { catalogs.Clear(); typeVersion++; }
            RefreshInputs();
        }
    }
    private bool IsPresentation(IPsiSourceFile file, PsiModuleChange.ChangeType type)
    {
        if (type is not (PsiModuleChange.ChangeType.Added or PsiModuleChange.ChangeType.Modified)) return false;
        try
        {
            var presentation = file.IsValid() && file.PsiModule is IAssemblyPsiModule assembly && assembly.IsValid()
                && file is PsiSourceFileFromPath && file.ToProjectFile() == null && file.Properties.ShouldBuildPsi
                && file.Properties.IsGeneratedFile && file.Properties.IsNonUserFile
                && !file.Properties.IsICacheParticipant && !file.Properties.ProvidesCodeModel;
            if (presentation) NotePresentation(ScriptDocuments.Id(file));
            return presentation;
        }
        catch (InvalidOperationException) { return false; }
    }
    public void NotePresentation(string documentId)
    {
        lock (gate)
        {
            if (closed) return;
            if (presentationDocuments.Count >= 2048) presentationDocuments.Clear();
            presentationDocuments.Add(documentId.Replace('\\', '/'));
        }
    }
    private bool RelevantProjectChange(ProjectModelChange change)
    {
        try
        {
            if (change.ProjectModelElement is IProjectItem item
                && (ReferenceEquals(change.GetNewProject(), psi.Solution.MiscFilesProject) || ReferenceEquals(change.GetOldProject(), psi.Solution.MiscFilesProject)))
            {
                lock (gate) if (presentationDocuments.Contains(item.Location.FullPath.Replace('\\', '/'))) return false;
            }
            var children = change.GetChildren().ToArray();
            var substantive = change.IsAdded || change.IsRemoved || change.IsMovedIn || change.IsMovedOut || change.IsExternallyChanged
                || change.IsPropertiesChanged || change.IsReferenceTargetChanged || change.IsTargetFrameworkChanged
                || change.ContainsChangeType(ProjectModelChangeType.PROJECT_STRUCTURE_AND_COMPONENTS_READY)
                || change.ContainsChangeType(ProjectModelChangeType.PROJECT_MODEL_CACHES_READY);
            return substantive || children.Length == 0 || children.Any(RelevantProjectChange);
        }
        catch (InvalidOperationException) { return true; }
    }
    public void TrackDocument(string key, IDocument document)
    {
        bool evicted = false;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (tracked.TryGetValue(key, out var previous) && ReferenceEquals(previous.Document, document)) return;
        }
        var current = new TrackedDocument(document, document.LastModificationStamp.ToString(), document.GetText());
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (tracked.Count >= 1024) { tracked.Clear(); evicted = true; }
            tracked[key] = current;
        }
        if (evicted) Invalidate(false);
    }
    private void RefreshInputs(IReadOnlyList<IDocument>? changed = null)
    {
        bool typesChanged, textChanged = false;
        string signature;
        try
        {
            var values = new List<string>();
            foreach (var module in psi.Modules.GetSourceModules().Where(module => module.IsValid() && module.ContainingProjectModule is IProject project
                && !ReferenceEquals(project, psi.Solution.MiscFilesProject) && !ReferenceEquals(project, psi.Solution.SolutionProject)))
            {
                values.Add("module:" + module.GetPersistentID() + "/" + module.TargetFrameworkId.UniqueString);
                foreach (var file in module.SourceFiles.Where(file => file.IsValid() && file.PrimaryPsiLanguage is CSharpLanguage))
                    values.Add("source:" + ScriptDocuments.Id(file) + "/" + file.InMemoryModificationStamp + "/" + file.ExternalModificationStamp + "/" + file.LastWriteTimeUtc.Ticks);
            }
            signature = string.Join("\n", values.Distinct().Order(StringComparer.Ordinal));
        }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        { Invalidate(true); return; }
        KeyValuePair<string, TrackedDocument>[] watched;
        lock (gate) { if (closed) return; watched = tracked.ToArray(); }
        var updates = new List<(string Key, TrackedDocument Before, TrackedDocument After)>();
        foreach (var pair in watched)
        {
            var value = pair.Value; var stamp = value.Document.LastModificationStamp.ToString();
            if (stamp == value.Stamp && (changed == null || !changed.Any(document => ReferenceEquals(document, value.Document)))) continue;
            updates.Add((pair.Key, value, new(value.Document, stamp, value.Document.GetText())));
        }
        lock (gate)
        {
            if (closed) return;
            typesChanged = inputSignature == null || inputSignature != signature; inputSignature = signature;
            foreach (var update in updates)
                if (tracked.TryGetValue(update.Key, out var current) && ReferenceEquals(current, update.Before))
                { textChanged |= update.Before.Text != update.After.Text; tracked[update.Key] = update.After; }
        }
        if (typesChanged || textChanged) Invalidate(typesChanged);
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
        TrackDocument(key, document);
        if (expectedEpoch.HasValue && expectedEpoch.Value != Epoch) throw new OperationCanceledException("Input inventory changed during analysis");
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
