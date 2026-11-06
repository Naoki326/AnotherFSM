using System.Runtime.CompilerServices;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation.Descriptors;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.Rider.Backend.Features.Usages;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace AnotherFSM.Rider;

public sealed record ReferenceQueryStatus(string ListId, long QueryEpoch, long ContextVersion, bool Owned, bool Updating);

/// <summary>Marks native queries expired without clearing, rerunning or reordering their rows.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ResultLifecycle
{
    private sealed class QueryState
    {
        public readonly object Gate = new();
        public long QueryEpoch = -1;
        public long Revision;
        public bool Known, Owned, Updating, HasRerun;
        public ReferenceQueryStatus? LastExpired;
    }
    private static readonly ILogger Log = Logger.GetLogger("AnotherFSM");
    private readonly ConditionalWeakTable<SearchDescriptor, QueryState> states = new();
    private readonly SearchDescriptorRegistrations registrations;
    private readonly RiderFindResultsBrowser browser;
    private readonly IPsiServices psi;
    private readonly ScriptIndex index;
    public event Action<IReadOnlyList<ReferenceQueryStatus>>? ReferencesExpired;

    public ResultLifecycle(Lifetime lifetime, ISolution solution, ScriptIndex index, SearchDescriptorRegistrations registrations,
        IPsiServices psi, RiderFindResultsBrowser browser)
    {
        this.index = index;
        this.registrations = registrations;
        this.psi = psi;
        this.browser = browser;
        registrations.SearchDescriptors.AddRemove.Advise(lifetime, args => {
            // SDK acknowledgement arguments are reused; never retain args in a closure/queue.
            var descriptor = args.Value;
            var adding = args.IsAdding;
            var acknowledging = args.IsAcknowledging;
            // Existing descriptors' zero-row history cannot be reconstructed from subscription replay.
            if (adding) Track(lifetime, descriptor, !acknowledging);
            else states.Remove(descriptor);
        });

        var deferred = 0;
        void Invalidate() => solution.Locks.ExecuteOrQueueReadLockEx(lifetime, "AnotherFSM expire references", () => {
            if (!psi.Files.AllDocumentsAreCommitted) Interlocked.Exchange(ref deferred, 1);
            var expired = new List<ReferenceQueryStatus>();
            foreach (var descriptor in registrations.SearchDescriptors.ToArray())
            {
                // Sessions are registered only AFTER the native model is created. No title/first-item fallback.
                var sessions = browser.Sessions.Values.Where(session => ReferenceEquals(session.SearchRequest, descriptor.Request)).Take(2).ToArray();
                if (sessions.Length != 1) continue;
                var status = GetStatus(sessions[0].UsageList.Model.ListId);
                if (status == null || !status.Owned || status.QueryEpoch >= status.ContextVersion
                    || !states.TryGetValue(descriptor, out var state)) continue;
                lock (state.Gate)
                {
                    if (state.QueryEpoch != status.QueryEpoch || state.Updating != status.Updating || state.LastExpired == status) continue;
                    state.LastExpired = status;
                }
                // Repaint invalid occurrences only. Late callbacks must never stop a newer native query.
                descriptor.OnUpdated();
                expired.Add(status);
            }
            if (expired.Count != 0) ReferencesExpired?.Invoke(expired.ToArray());
        });
        psi.Files.ObserveAfterCommit(lifetime, () => { if (Interlocked.Exchange(ref deferred, 0) != 0) Invalidate(); });
        index.Invalidated += Invalidate;
        lifetime.OnTermination(() => { index.Invalidated -= Invalidate; ReferencesExpired = null; });
    }

    private bool IsOwned(SearchDescriptor descriptor) => descriptor.Request.SearchTargets.OfType<IDeclaredElementEnvoy>()
        .Select(target => target.GetValidDeclaredElement()).Any(target => target != null && FsmSearcherFactory.IsTarget(target));

    private void Capture(SearchDescriptor descriptor, QueryState state, bool rerun)
    {
        // Synchronous at creation / native Rerun(true), NOT queued until after a PSI change.
        using var readLock = ReadLockCookie.Create("AnotherFSM.QueryIdentity", "Capture");
        var epoch = index.Epoch;
        var committed = psi.Files.AllDocumentsAreCommitted;
        var owned = committed && IsOwned(descriptor);
        lock (state.Gate)
        {
            state.QueryEpoch = epoch;
            state.Known = committed;
            state.Owned |= owned; // Preserve past ownership even when an attribute is removed and there were no DSL rows.
            state.HasRerun |= rerun;
            if (rerun) state.Updating = true;
            state.Revision++;
        }
        // No delayed current-PSI read may masquerade as evidence about this query's creation.
        // Real FSM rows can establish its epoch/ownership later; otherwise status stays unknown until Rerun.
        if (!committed) Log.Warn("AFSM32 query identity capture before PSI commit; reference status remains unknown");
    }

    private void Track(Lifetime solutionLifetime, SearchDescriptor descriptor, bool created)
    {
        QueryState state;
        lock (states)
        {
            if (states.TryGetValue(descriptor, out _)) return;
            state = new QueryState { Updating = descriptor.IsUpdatingContent.Value };
            states.Add(descriptor, state);
        }
        var scope = Lifetime.Intersect(solutionLifetime, descriptor.LifetimeDefinition.Lifetime);
        scope.OnTermination(() => states.Remove(descriptor));
        if (!scope.IsAlive) return;
        if (created) Capture(descriptor, state, false);
        descriptor.IsUpdatingContent.Change.Advise_HasNew(scope, args => {
            var updating = args.New;
            var acknowledging = args.IsAcknowledging;
            // Native async Rerun sets true under its committed PSI read lock, before clearing old rows.
            // Capture the WHOLE query, including zero-result queries; never advance it per file/status request.
            if (updating && !acknowledging) Capture(descriptor, state, true);
            lock (state.Gate) state.Updating = updating;
        });
    }

    /// <summary>Caller must hold the host read lock (BridgeQueries uses CommitWithRetryMainRead).</summary>
    public ReferenceQueryStatus? GetStatus(string listId)
    {
        if (string.IsNullOrEmpty(listId) || !browser.Sessions.TryGetValue(listId, out var session)
            || session.UsageList.Model.ListId != listId || session.Model.Usages.ListId != listId) return null;
        // SDK TryGetSession uses Equals. Only unique REQUEST REFERENCE identity is safe in either direction.
        if (browser.Sessions.Values.Count(candidate => ReferenceEquals(candidate.SearchRequest, session.SearchRequest)) != 1) return null;
        var descriptors = registrations.SearchDescriptors.Where(descriptor => descriptor.LifetimeDefinition.Lifetime.IsAlive
            && ReferenceEquals(descriptor.Request, session.SearchRequest)).Take(2).ToArray();
        if (descriptors.Length != 1 || !states.TryGetValue(descriptors[0], out var state)) return null;
        var descriptor = descriptors[0];
        long revision;
        lock (state.Gate) revision = state.Revision;
        long[] generations;
        // Do not acquire descriptor locks while holding Gate: native property events can run under its write lock.
        descriptor.RwLock.ReadLock.Acquire();
        try { generations = descriptor.Items.OfType<FsmOccurrence>().Select(row => row.Generation).Distinct().Take(2).ToArray(); }
        finally { descriptor.RwLock.ReadLock.Release(); }
        var currentOwned = psi.Files.AllDocumentsAreCommitted && IsOwned(descriptor);
        lock (state.Gate)
        {
            if (revision != state.Revision || generations.Length > 1) return null;
            state.Owned |= currentOwned || generations.Length != 0;
            // Initial synchronous search may precede descriptor construction. Actual rows prove its real epoch.
            // At Rerun(true), rows from BEFORE native Clear must not overwrite the newly captured epoch.
            if (generations.Length == 1 && (!state.HasRerun || generations[0] >= state.QueryEpoch))
            {
                state.QueryEpoch = generations[0];
                state.Known = true;
            }
            return state.Known ? new(listId, state.QueryEpoch, index.Epoch, state.Owned, state.Updating) : null;
        }
    }
}
