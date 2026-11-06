using System.Runtime.CompilerServices;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation.Descriptors;
using JetBrains.ReSharper.Feature.Services.Occurrences;
using JetBrains.ReSharper.Psi;

namespace AnotherFSM.Rider;

/// <summary>Marks native queries expired without clearing, rerunning or reordering their rows.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ResultLifecycle
{
    private sealed class Expiration { public long MarkedEpoch = -1; public bool Owned; }
    private readonly ConditionalWeakTable<SearchDescriptor, Expiration> expired = new();
    public event Action<IReadOnlyList<string>>? ReferencesExpired;
    public ResultLifecycle(Lifetime lifetime, ISolution solution, ScriptIndex index, SearchDescriptorRegistrations registrations, IPsiServices psi)
    {
        var deferred = false;
        void Invalidate() => solution.Locks.ExecuteOrQueueReadLockEx(lifetime, "AnotherFSM expire references", () => {
            var titles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var descriptor in registrations.SearchDescriptors.ToArray())
            {
                IOccurrence[] rows;
                descriptor.RwLock.ReadLock.Acquire();
                try { rows = descriptor.Items.ToArray(); }
                finally { descriptor.RwLock.ReadLock.Release(); }
                var own = rows.OfType<FsmOccurrence>().ToArray();
                // A delayed invalidation never interrupts a newer explicitly refreshed query.
                if (own.Any(row => !row.Expired)) continue;
                var state = expired.GetValue(descriptor, _ => new Expiration());
                if (!state.Owned)
                {
                    if (own.Length != 0) state.Owned = true;
                    else if (!psi.Files.AllDocumentsAreCommitted) { deferred = true; continue; }
                    else state.Owned = descriptor.Request.SearchTargets.OfType<IDeclaredElementEnvoy>()
                        .Select(target => target.GetValidDeclaredElement()).Any(target => target != null && FsmSearcherFactory.IsTarget(target));
                }
                if (!state.Owned || state.MarkedEpoch == index.Epoch) continue;
                state.MarkedEpoch = index.Epoch;
                // Native invalid entries may be hidden by Rider: label the actual frontend tab too.
                // Do not Clear/Stop here: late callbacks must not stop or mix a newer query.
                descriptor.OnUpdated();
                titles.Add(descriptor.Title.Value);
            }
            if (titles.Count != 0) ReferencesExpired?.Invoke(titles.ToArray());
        });
        psi.Files.ObserveAfterCommit(lifetime, () => { if (deferred) { deferred = false; Invalidate(); } });
        index.Invalidated += Invalidate;
        lifetime.OnTermination(() => { index.Invalidated -= Invalidate; ReferencesExpired = null; });
    }
}
