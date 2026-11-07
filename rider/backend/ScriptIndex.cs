using System.Security.Cryptography;
using System.Text;
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
    private readonly IndexDiagnostics diagnostics = new();
    private IndexState? observedDiagnosticState;
    private string? diagnosticInputSummary;
    public IReadOnlyList<IndexDiagnostic> DiagnosticRecords => diagnostics.Snapshot();
    internal IndexState DiagnosticState { get { lock (gate) return CaptureDiagnosticState(); } }
    internal IndexDiagnostics.RequestScope BeginDiagnosticRequest(string requestId) => diagnostics.BeginRequest(requestId, DiagnosticState);
    private string? inputSignature;
    private readonly HashSet<string> presentationDocuments = new(StringComparer.OrdinalIgnoreCase);
    private sealed record TrackedDocument(IDocument Document, string Stamp, string Text)
    {
        public string InputSummary { get; } = IndexDiagnostics.Summarize(Text);
    }
    private readonly Dictionary<string, TrackedDocument> tracked = new(StringComparer.OrdinalIgnoreCase);
    private FsmWorkspace documents = new();
    private sealed record CatalogSnapshot(int PsiTimestamp, IReadOnlyList<NodeType> Types);
    private readonly Dictionary<string, CatalogSnapshot> catalogs = new();
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
        RecordObservation(IndexDiagnosticCause.SolutionOpened, IndexDiagnosticSource.Lifetime, null);
        changes.Changed.Advise(lifetime, args => {
            var moduleStructural = args.ChangeMap.GetChanges<PsiModuleChange>().Any(change => change.AssemblyChanges.Count > 0
                || change.ModuleChanges.Count > 0 || change.FileChanges.Any(file => !IsPresentation(file.Item, file.Type)));
            var structural = moduleStructural || args.ChangeMap.GetChanges<ProjectModelChange>().Any(RelevantProjectChange);
            var fileSystem = args.ChangeMap.GetChanges<FileSystemChange>().Any(change => change.Deltas.Any(ScriptChanged));
            if (structural || fileSystem)
                Invalidate(structural, structural, fileSystem,
                    structural ? IndexDiagnosticCause.StructureChange : IndexDiagnosticCause.InventoryChange,
                    moduleStructural ? IndexDiagnosticSource.PsiModuleChange
                        : structural ? IndexDiagnosticSource.ProjectModelChange : IndexDiagnosticSource.FileSystem);
            else
            {
                var changed = args.ChangeMap.GetChanges<DocumentChange>().Select(change => change.Document).ToArray();
                if (changed.Length > 0) RefreshInputs(IndexDiagnosticSource.DocumentChange, changed);
                else lock (gate) RecordObservation(observedDiagnosticState?.PsiCacheTimestamp != psi.Files.PsiCacheTimestamp
                    ? IndexDiagnosticCause.CacheFreshnessOnly : IndexDiagnosticCause.NoBusinessChange, IndexDiagnosticSource.ChangeManager, null);
            }
        });
        psi.Files.AfterPsiChanged += OnPsiChanged;
        lifetime.OnTermination(() => {
            psi.Files.AfterPsiChanged -= OnPsiChanged;
            lock (gate) { closed = true; documents.Dispose(); catalogs.Clear(); tracked.Clear(); presentationDocuments.Clear(); }
            Invalidate(true, cause: IndexDiagnosticCause.SolutionClosed, source: IndexDiagnosticSource.Lifetime);
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
        if (source != null && IsPresentation(source, PsiModuleChange.ChangeType.Modified))
        {
            lock (gate) RecordObservation(IndexDiagnosticCause.CacheFreshnessOnly, IndexDiagnosticSource.PresentationPsi, null);
            return;
        }
        // Broad source PSI also describes SDK-generated external views. Check actual business inputs,
        // never the global PSI timestamp; real assembly/module deltas remain unconditional above.
        if (source?.PrimaryPsiLanguage is CSharpLanguage && source.PsiModule.ContainingProjectModule is IProject)
            RefreshInputs(IndexDiagnosticSource.ProjectCSharpPsi);
        else if (node == null)
        {
            // SDK metadata resolution can advance without a project-input change. Never retain
            // a partial symbol scope across that notification. This is cache freshness, not proof
            // that business bindings changed: generated external views must not expire UI results.
            lock (gate) catalogs.Clear();
            RefreshInputs(IndexDiagnosticSource.NullPsi);
        }
        else
        {
            lock (gate) RecordObservation(IndexDiagnosticCause.CacheFreshnessOnly, IndexDiagnosticSource.OtherPsi, null);
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
            UpdateDiagnosticSummary();
            RecordObservation(IndexDiagnosticCause.InputTracked, IndexDiagnosticSource.TrackDocument, null);
        }
        if (evicted) Invalidate(false, cause: IndexDiagnosticCause.TrackingEvicted, source: IndexDiagnosticSource.TrackDocument);
    }
    private void RefreshInputs(IndexDiagnosticSource source, IReadOnlyList<IDocument>? changed = null)
    {
        bool typesChanged, initialized, textChanged = false;
        string signature;
        try
        {
            var values = new List<string>();
            foreach (var module in psi.Modules.GetSourceModules().Where(module => module.IsValid() && module.ContainingProjectModule is IProject project
                && !ReferenceEquals(project, psi.Solution.MiscFilesProject) && !ReferenceEquals(project, psi.Solution.SolutionProject)))
            {
                values.Add("module:" + module.GetPersistentID() + "/" + module.TargetFrameworkId.UniqueString);
                foreach (var file in module.SourceFiles.Where(file => file.IsValid() && file.PrimaryPsiLanguage is CSharpLanguage))
                    // PSI tree/cache construction can change source-file stamps without an edit.
                    // Actual host text includes unsaved C# input; identical bytes do not expire a query.
                    values.Add("source:" + ScriptDocuments.Id(file) + "/"
                        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Document.GetText()))));
            }
            signature = string.Join("\n", values.Distinct().Order(StringComparer.Ordinal));
        }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        { Invalidate(true, cause: IndexDiagnosticCause.InputObservationFailed, source: source); return; }
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
            initialized = inputSignature != null;
            typesChanged = inputSignature == null || inputSignature != signature; inputSignature = signature;
            foreach (var update in updates)
                if (tracked.TryGetValue(update.Key, out var current) && ReferenceEquals(current, update.Before))
                { textChanged |= update.Before.Text != update.After.Text; tracked[update.Key] = update.After; }
            if (typesChanged || textChanged) UpdateDiagnosticSummary();
            else RecordObservation(source == IndexDiagnosticSource.NullPsi || observedDiagnosticState?.PsiCacheTimestamp != psi.Files.PsiCacheTimestamp
                ? IndexDiagnosticCause.CacheFreshnessOnly : IndexDiagnosticCause.NoBusinessChange, source, false);
        }
        if (typesChanged || textChanged) Invalidate(typesChanged,
            cause: !initialized && !textChanged ? IndexDiagnosticCause.InputTracked : IndexDiagnosticCause.ActualTextChange,
            source: source, inputChanged: initialized || textChanged ? true : null);
    }
    // Called only under gate, from existing observations. The source signature contains IDs/hashes;
    // journal data gets a further digest, including already-tracked DSL/config input, never raw text.
    private void UpdateDiagnosticSummary() => diagnosticInputSummary = IndexDiagnostics.Summarize(
        (inputSignature == null ? "unknown" : IndexDiagnostics.Summarize(inputSignature)) + "\n"
        + string.Join("\n", tracked.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            IndexDiagnostics.Summarize(pair.Key) + ":" + pair.Value.InputSummary)));
    private IndexState CaptureDiagnosticState() => new(epoch, typeVersion, structureVersion, psi.Files.PsiCacheTimestamp, diagnosticInputSummary);
    private void RecordObservation(IndexDiagnosticCause cause, IndexDiagnosticSource source, bool? inputChanged)
    {
        var after = CaptureDiagnosticState();
        // Before is the last observed index state, retaining SDK stamp transitions which preceded
        // the notification. Sampling/reading diagnostics never advances this observation baseline.
        diagnostics.Record(observedDiagnosticState ?? after, after, cause, source, inputChanged);
        observedDiagnosticState = after;
    }
    private void Invalidate(bool typesChanged, bool documentSetChanged = false, bool inventoryChanged = false,
        IndexDiagnosticCause cause = IndexDiagnosticCause.StructureChange, IndexDiagnosticSource source = IndexDiagnosticSource.ChangeManagerStructure,
        bool? inputChanged = null)
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
            RecordObservation(cause, source, inputChanged);
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
        // This SDK timestamp also covers changes which do not publish AfterPsiChanged.
        // It expires only the derived catalog, not the identity/epoch of displayed results.
        var timestamp = psi.Files.PsiCacheTimestamp;
        long expectedVersion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var reusable = catalogs.TryGetValue(key, out var cached) && cached.PsiTimestamp == timestamp;
            RecordObservation(observedDiagnosticState?.PsiCacheTimestamp != timestamp
                || cached != null && cached.PsiTimestamp != timestamp
                ? IndexDiagnosticCause.CacheFreshnessOnly : IndexDiagnosticCause.NoBusinessChange,
                IndexDiagnosticSource.Catalog, null); // Catalog freshness does not inspect business input.
            if (reusable) return cached!.Types;
            expectedVersion = typeVersion;
        }
        var current = TypeCatalog.Read(module);
        lock (gate)
        {
            if (closed || expectedVersion != typeVersion) throw new OperationCanceledException("AnotherFSM type context changed");
            // A cache-only SDK change must not cancel a valid user operation. The committed
            // read remains usable, but do not cache it across a stamp change during lazy resolution.
            if (timestamp == psi.Files.PsiCacheTimestamp)
            {
                if (catalogs.Count >= 128) catalogs.Clear();
                catalogs[key] = new(timestamp, current);
            }
            RecordObservation(timestamp != psi.Files.PsiCacheTimestamp
                ? IndexDiagnosticCause.CacheFreshnessOnly : IndexDiagnosticCause.NoBusinessChange, IndexDiagnosticSource.Catalog, null);
        }
        return current;
    }
}
