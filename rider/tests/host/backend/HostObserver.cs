using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AnotherFSM.Rider;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation.Descriptors;
using JetBrains.ReSharper.Feature.Services.Occurrences;
using JetBrains.ReSharper.Feature.Services.Tree;
using JetBrains.ReSharper.Psi;

namespace AnotherFSM.Acceptance;

/// <summary>Read-only observation of the real native search. Never calls Search/Clear/Stop.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class HostObserver
{
    private sealed class Identity { public int Id; public bool Matched; public bool Subscribed; public ObservedRow? LastSnapshot; }
    private readonly ConditionalWeakTable<object, Identity> identities = new();
    private readonly List<object> events = new();
    private readonly object gate = new();
    private readonly DateTime start = DateTime.UtcNow;
    private int nextId;
    private int queued;
    private long sequence;
    private int nullPsiNotifications;
    private Timer? timer;
    private int Id(object value) => identities.GetValue(value, _ => new Identity { Id = Interlocked.Increment(ref nextId) }).Id;

    public HostObserver(Lifetime lifetime, ISolution solution, SearchDescriptorRegistrations registrations, IPsiServices psi, ScriptIndex index)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var runId = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_RUN_ID");
        var backendCreationFileTime = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc();
        if (Guid.TryParseExact(runId, "D", out _))
        {
            // Immutable, per-process identity survives observer replacement and backend restarts.
            // Write BEFORE waiting for a solution read lock; cleanup must not depend on PSI readiness.
            var target = Path.Combine(directory, $"backend-identity-{Environment.ProcessId}-{backendCreationFileTime}.json");
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { runId, backendPid = Environment.ProcessId, backendCreationFileTime }));
            File.Move(temporary, target, true);
        }
        void ObservePsi(JetBrains.ReSharper.Psi.Tree.ITreeNode? node, PsiChangedElementType kind)
        { if (node == null) Interlocked.Increment(ref nullPsiNotifications); }
        psi.Files.AfterPsiChanged += ObservePsi;
        lifetime.OnTermination(() => psi.Files.AfterPsiChanged -= ObservePsi);
        timer = new Timer(_ => {
            // The chained #32 + #33 journeys and transport check have independent bounded deadlines.
            // Never let the observer stop halfway and mistake a previous sample for current evidence.
            if (!lifetime.IsAlive || DateTime.UtcNow - start > TimeSpan.FromMinutes(20)) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.TestObserver.Sample", () => {
                try
                {
                    var snapshots = registrations.SearchDescriptors.Select(descriptor => Sample(lifetime, descriptor)).ToArray();
                    object[] changeEvents;
                    lock (gate) changeEvents = events.ToArray();
                    var json = JsonSerializer.Serialize(new { runId, backendPid = Environment.ProcessId, backendCreationFileTime, seq = Interlocked.Increment(ref sequence),
                        elapsedMs = (DateTime.UtcNow - start).TotalMilliseconds, epoch = index.Epoch, nullPsiNotifications, descriptors = snapshots, events = changeEvents });
                    _ = Task.Run(() => {
                        try
                        {
                            var target = Path.Combine(directory, "native-observation.json");
                            File.WriteAllText(target + ".tmp", json);
                            File.Move(target + ".tmp", target, true);
                        }
                        finally { Interlocked.Exchange(ref queued, 0); }
                    });
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(directory, "observer-error.txt"), error.ToString());
                    Interlocked.Exchange(ref queued, 0);
                }
            });
        }, null, 0, 250);
        lifetime.OnTermination(() => timer.Dispose());
    }
    private object Sample(Lifetime lifetime, SearchDescriptor descriptor)
    {
        var state = identities.GetValue(descriptor, _ => new Identity { Id = Interlocked.Increment(ref nextId) });
        if (!state.Subscribed)
        {
            state.Subscribed = true;
            descriptor.ItemArrived.Advise(lifetime, change => {
                var kind = change.Action.ToString();
                var added = change.NewItems?.Cast<object>().Select(Id).ToArray() ?? Array.Empty<int>();
                var removed = change.OldItems?.Cast<object>().Select(Id).ToArray() ?? Array.Empty<int>();
                lock (gate)
                {
                    if (events.Count == 512) events.RemoveAt(0);
                    events.Add(new { descriptorId = state.Id, action = kind, added, removed });
                }
            });
        }
        var asyncDescriptor = (IAsyncOccurrenceBrowserDescriptor)descriptor;
        asyncDescriptor.RwLock.ReadLock.Acquire();
        try
        {
            var rows = descriptor.Items.Select(Row).ToArray();
            state.Matched |= rows.Any(row => row.runtimeType == "AnotherFSM.Rider.FsmOccurrence");
            return new { id = state.Id, matched = state.Matched, title = descriptor.Title.Value,
                requestType = descriptor.Request.GetType().FullName, updating = descriptor.IsUpdatingContent.Value,
                busy = asyncDescriptor.IsBusy.Value, items = rows };
        }
        finally { asyncDescriptor.RwLock.ReadLock.Release(); }
    }
    private sealed record ObservedRow(int id, string? runtimeType, string file, int start, int length, bool valid,
        string text, string currentSlice, string? documentType, string? documentStamp, string? error, bool snapshotOnly = false, long? queryEpoch = null);
    private ObservedRow Row(IOccurrence occurrence)
    {
        var state = identities.GetValue(occurrence, _ => new Identity { Id = Interlocked.Increment(ref nextId) });
        var id = state.Id;
        var type = occurrence.GetType().FullName;
        var generation = occurrence.GetType().GetProperty("Generation")?.GetValue(occurrence) as long?;
        try
        {
            if (occurrence is not RangeOccurrence range) return new(id, type, "", -1, 0, occurrence.IsValid, "", "", null, null, null);
            var documentRange = range.GetDocumentRange();
            var span = range.GetTextRange();
            if (documentRange.Document == null) throw new InvalidOperationException("Host source document unavailable after context removal");
            var validRange = span.IsValid && span.StartOffset >= 0 && span.EndOffset <= documentRange.Document.GetTextLength();
            var path = (range.GetProjectFile()?.Location ?? range.SourceFile.GetLocation()).FullPath.Replace('\\', '/');
            return state.LastSnapshot = new(id, type, path, span.StartOffset, span.EndOffset - span.StartOffset, occurrence.IsValid,
                range.GetDisplayNameText(), validRange ? documentRange.Document.GetText(span) : "",
                documentRange.Document.GetType().FullName, documentRange.Document.LastModificationStamp.ToString(), null, false, generation);
        }
        catch (Exception error)
        {
            // Unloading a project can remove the host document. Preserve last-observed plain data
            // for row identity/order evidence, explicitly NOT a current range or valid navigation target.
            return state.LastSnapshot is { } snapshot
                ? snapshot with { valid = false, error = error.GetType().Name, snapshotOnly = true }
                : new(id, type, "", -1, 0, false, "", "", null, null, error.GetType().Name, true);
        }
    }
}
