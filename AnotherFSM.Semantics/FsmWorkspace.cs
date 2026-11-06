namespace AnotherFSM.Semantics;

public sealed record SnapshotStamp(Guid Session, long ContextVersion, string DocumentId, long DocumentVersion);
public sealed record VersionedAnalysis(SnapshotStamp Stamp, DocumentAnalysis Analysis);

/// <summary>One solution lifetime; one current parse per document, with bounded retention.</summary>
public sealed class FsmWorkspace : IDisposable
{
    private sealed class Entry(DocumentSnapshot document, CancellationTokenSource cancellation, long use)
    {
        public DocumentSnapshot Document = document;
        public CancellationTokenSource Cancellation = cancellation;
        public DocumentAnalysis? Analysis;
        public long Use = use;
        public bool Finished;
        public void Cancel() { if (!Finished) Cancellation.Cancel(); }
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> documents = new(StringComparer.Ordinal);
    private readonly Guid session = Guid.NewGuid();
    private readonly int capacity;
    private long contextVersion;
    private long use;
    private bool disposed;
    public event Action? Invalidated;
    public FsmWorkspace(int capacity = 512)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    public async Task<VersionedAnalysis?> UpdateAsync(DocumentSnapshot document, CancellationToken cancellation = default)
    {
        var (entry, cached) = Begin(document, cancellation);
        if (entry == null) return cached;
        return await Task.Run(() => Complete(entry, cancellation, null)).ConfigureAwait(false);
    }

    /// <summary>Synchronous host-search seam: computes on the caller's background read thread, never waits on a task.</summary>
    public VersionedAnalysis? Update(DocumentSnapshot document, CancellationToken cancellation = default, Action? checkHostCancellation = null)
    {
        var (entry, cached) = Begin(document, cancellation);
        return entry == null ? cached : Complete(entry, cancellation, checkHostCancellation);
    }

    private (Entry? Entry, VersionedAnalysis? Cached) Begin(DocumentSnapshot document, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (documents.TryGetValue(document.Id, out var existing))
            {
                if (document.Version < existing.Document.Version) return (null, null);
                if (document.Version == existing.Document.Version)
                {
                    if (document.Text != existing.Document.Text) return (null, null);
                    if (existing.Analysis != null) return (null, Current(existing));
                }
                existing.Cancel();
            }
            if (!documents.ContainsKey(document.Id) && documents.Count >= capacity)
            {
                var oldest = documents.MinBy(p => p.Value.Use);
                oldest.Value.Cancel();
                documents.Remove(oldest.Key);
            }
            var entry = new Entry(document, CancellationTokenSource.CreateLinkedTokenSource(cancellation), ++use);
            documents[document.Id] = entry;
            contextVersion++;
            return (entry, null);
        }
    }
    private VersionedAnalysis? Complete(Entry entry, CancellationToken cancellation, Action? checkHostCancellation)
    {
        var document = entry.Document;
        try
        {
            Invalidated?.Invoke();
            var analysis = FsmAnalysis.Analyze(document, entry.Cancellation.Token, checkHostCancellation);
            lock (gate)
            {
                if (disposed || !documents.TryGetValue(document.Id, out var current) || !ReferenceEquals(current, entry)
                    || entry.Cancellation.IsCancellationRequested) return null;
                entry.Analysis = analysis;
                return Current(entry);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
        finally
        {
            lock (gate)
            {
                entry.Finished = true;
                entry.Cancellation.Dispose();
                if (entry.Analysis == null && documents.TryGetValue(document.Id, out var current) && ReferenceEquals(current, entry))
                    documents.Remove(document.Id);
            }
        }
    }
    public VersionedAnalysis? GetCurrent(string documentId)
    {
        lock (gate)
        {
            if (disposed || !documents.TryGetValue(documentId, out var entry) || entry.Analysis == null) return null;
            entry.Use = ++use;
            return Current(entry);
        }
    }
    public bool IsCurrent(SnapshotStamp stamp)
    {
        lock (gate) return !disposed && stamp.Session == session && stamp.ContextVersion == contextVersion
            && documents.TryGetValue(stamp.DocumentId, out var entry) && entry.Analysis != null
            && stamp.DocumentVersion == entry.Document.Version;
    }
    public void InvalidateContext()
    {
        lock (gate) { if (disposed) return; contextVersion++; }
        Invalidated?.Invoke();
    }
    public void Remove(string documentId)
    {
        lock (gate)
        {
            if (disposed || !documents.Remove(documentId, out var entry)) return;
            entry.Cancel();
            contextVersion++;
        }
        Invalidated?.Invoke();
    }
    private VersionedAnalysis Current(Entry entry) => new(new(session, contextVersion, entry.Document.Id, entry.Document.Version), entry.Analysis!);
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in documents.Values) entry.Cancel();
            documents.Clear();
        }
        Invalidated?.Invoke();
        Invalidated = null;
    }
}
