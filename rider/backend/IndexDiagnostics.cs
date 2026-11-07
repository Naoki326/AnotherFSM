using System.Security.Cryptography;
using System.Text;

namespace AnotherFSM.Rider;

// Deliberately SDK/semantics-free: no PSI objects, paths, payloads, exception text or callbacks.
public sealed record IndexState(long Epoch, long TypeVersion, long StructureVersion, int PsiCacheTimestamp, string? InputSummary);
public enum IndexDiagnosticCause
{
    NoBusinessChange, CacheFreshnessOnly, ActualTextChange, StructureChange, InventoryChange,
    InputTracked, InputObservationFailed, TrackingEvicted, SolutionOpened, SolutionClosed, RequestStage, Cancelled, Reply
}
public enum IndexDiagnosticSource
{
    DocumentChange, ChangeManager, ChangeManagerStructure, PsiModuleChange, ProjectModelChange,
    FileSystem, ProjectCSharpPsi, NullPsi, PresentationPsi,
    OtherPsi, TrackDocument, Catalog, Lifetime, BridgeQuery
}
public enum IndexDiagnosticStage { Observation, Submitted, Commit, Resolve, Navigate, Reply }
public enum IndexDiagnosticCancellation { None, Unknown }
public enum IndexDiagnosticStatus
{
    Unknown, InvalidRequest, UnknownQuery, ReferenceStatus, Stale, Unsupported, Analyzing, Hosts,
    Unresolved, Metadata, Dsl, Type, Locations, NavigationDispatched, Unavailable,
    MetadataNavigationDispatched, DecompiledNavigationDispatched
}
public sealed record IndexDiagnostic(long Sequence, IndexState Before, IndexState After,
    IndexDiagnosticCause Cause, IndexDiagnosticSource Source, IndexDiagnosticStage Stage,
    string? RequestId, bool? InputChanged, IndexDiagnosticStatus? Status,
    IndexDiagnosticCancellation Cancellation);

/// <summary>Bounded in-memory plain data only. Reading a snapshot cannot refresh/invalidate an index.</summary>
public sealed class IndexDiagnostics
{
    private readonly object gate = new();
    private readonly Queue<IndexDiagnostic> records = new();
    private readonly AsyncLocal<RequestScope?> currentRequest = new();
    private readonly int capacity;
    private long sequence;

    public IndexDiagnostics(int capacity = 512)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    public static string Summarize(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    private static string? Summary(string? value) => value == null ? null
        : value.Length == 64 && value.All(Uri.IsHexDigit) ? value : Summarize(value);
    private static IndexState Sanitize(IndexState value) => value with { InputSummary = Summary(value.InputSummary) };
    // The UI emits UUIDs. Preserve those for correlation; arbitrary IDs may contain payloads
    // or credentials, so retain only a bounded digest for all other shapes.
    private static string SafeRequestId(string value) => Guid.TryParseExact(value, "D", out _) ? value : "sha256:" + Summarize(value);

    public IReadOnlyList<IndexDiagnostic> Snapshot()
    {
        lock (gate) return Array.AsReadOnly(records.ToArray());
    }

    public void Record(IndexState before, IndexState after, IndexDiagnosticCause cause, IndexDiagnosticSource source, bool? inputChanged)
    {
        var request = currentRequest.Value;
        Append(before, after, cause, source, request?.Stage ?? IndexDiagnosticStage.Observation,
            request?.RequestId, inputChanged, null, IndexDiagnosticCancellation.None);
    }

    private void Append(IndexState before, IndexState after, IndexDiagnosticCause cause, IndexDiagnosticSource source,
        IndexDiagnosticStage stage, string? requestId, bool? inputChanged, IndexDiagnosticStatus? status, IndexDiagnosticCancellation cancellation)
    {
        before = Sanitize(before); after = Sanitize(after);
        lock (gate)
        {
            if (records.Count == capacity) records.Dequeue();
            records.Enqueue(new(++sequence, before, after, cause, source, stage, requestId, inputChanged, status, cancellation));
        }
    }

    public RequestScope BeginRequest(string requestId, IndexState state) => new(this, SafeRequestId(requestId), state);

    public sealed class RequestScope : IDisposable
    {
        private readonly IndexDiagnostics owner;
        private readonly RequestScope? previous;
        private readonly object gate = new();
        private IndexState before;
        private IndexDiagnosticStage stage;
        internal string RequestId { get; }
        public IndexDiagnosticStage Stage { get { lock (gate) return stage; } }

        internal RequestScope(IndexDiagnostics owner, string requestId, IndexState state)
        {
            this.owner = owner; RequestId = requestId; before = Sanitize(state);
            previous = owner.currentRequest.Value;
            owner.currentRequest.Value = this;
            Enter(IndexDiagnosticStage.Submitted, state);
        }
        // Host schedulers need not flow ExecutionContext. Explicitly bind attribution inside
        // their callbacks, without changing the scheduler, request lifetime or cancellation.
        public IDisposable Activate() => new Attribution(owner, this);
        private sealed class Attribution : IDisposable
        {
            private readonly IndexDiagnostics owner;
            private readonly RequestScope? previous;
            public Attribution(IndexDiagnostics owner, RequestScope request)
            {
                this.owner = owner; previous = owner.currentRequest.Value;
                owner.currentRequest.Value = request;
            }
            public void Dispose() => owner.currentRequest.Value = previous;
        }
        public void Enter(IndexDiagnosticStage next, IndexState state)
        {
            lock (gate)
            {
                before = Sanitize(state); stage = next;
                owner.Append(before, before, IndexDiagnosticCause.RequestStage, IndexDiagnosticSource.BridgeQuery,
                    next, RequestId, null, null, IndexDiagnosticCancellation.None);
            }
        }
        public void Complete(IndexDiagnosticStatus status, IndexState state)
        {
            lock (gate) owner.Append(before, state, IndexDiagnosticCause.Reply, IndexDiagnosticSource.BridgeQuery,
                IndexDiagnosticStage.Reply, RequestId, null, status, IndexDiagnosticCancellation.None);
        }
        // Cancellation is observed, not inferred from epoch/stamp/lifetime correlation.
        // Stage remains the last actually entered phase, including Commit before a host read starts.
        public void Cancel(IndexState state)
        {
            lock (gate) owner.Append(before, state, IndexDiagnosticCause.Cancelled,
                IndexDiagnosticSource.BridgeQuery, stage, RequestId, null, null, IndexDiagnosticCancellation.Unknown);
        }
        public void Dispose() => owner.currentRequest.Value = previous;
    }
}
