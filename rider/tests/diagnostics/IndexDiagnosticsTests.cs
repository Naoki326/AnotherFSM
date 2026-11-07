using System.Text.Json;
using AnotherFSM.Rider;

public sealed class IndexDiagnosticsTests
{
    private static IndexState State(long epoch = 1, int stamp = 10, string? summary = null) => new(epoch, 2, 3, stamp, summary);
    private static string RequestId(int i) => new Guid(i, 0, 0, new byte[8]).ToString("D");

    [Fact]
    public void Journal_is_bounded_ordered_and_snapshot_cannot_be_mutated()
    {
        var journal = new IndexDiagnostics(2);
        journal.Record(State(), State(), IndexDiagnosticCause.NoBusinessChange, IndexDiagnosticSource.DocumentChange, false);
        var old = journal.Snapshot();
        for (var i = 0; i < 3; i++)
            journal.Record(State(), State(), IndexDiagnosticCause.NoBusinessChange, IndexDiagnosticSource.DocumentChange, false);
        Assert.Single(old);
        Assert.Equal(1, old[0].Sequence);
        Assert.Equal(new long[] { 3, 4 }, journal.Snapshot().Select(item => item.Sequence));
        Assert.Throws<NotSupportedException>(() => ((IList<IndexDiagnostic>)old).Clear());
    }

    [Fact]
    public void Observation_preserves_both_states_and_verified_input_change()
    {
        var journal = new IndexDiagnostics();
        var before = State(summary: IndexDiagnostics.Summarize("old-signature"));
        var after = new IndexState(2, 4, 5, 11, IndexDiagnostics.Summarize("new-signature"));
        journal.Record(before, after, IndexDiagnosticCause.ActualTextChange, IndexDiagnosticSource.ProjectCSharpPsi, true);
        var record = Assert.Single(journal.Snapshot());
        Assert.Equal(before, record.Before);
        Assert.Equal(after, record.After);
        Assert.True(record.InputChanged);
        Assert.Equal(IndexDiagnosticStage.Observation, record.Stage);
    }

    [Fact]
    public void Cache_freshness_observation_does_not_require_business_state_mutation()
    {
        var journal = new IndexDiagnostics();
        journal.Record(State(), State(stamp: 11), IndexDiagnosticCause.CacheFreshnessOnly, IndexDiagnosticSource.NullPsi, false);
        var record = Assert.Single(journal.Snapshot());
        Assert.Equal(record.Before.Epoch, record.After.Epoch);
        Assert.Equal(record.Before.TypeVersion, record.After.TypeVersion);
        Assert.Equal(record.Before.StructureVersion, record.After.StructureVersion);
        Assert.NotEqual(record.Before.PsiCacheTimestamp, record.After.PsiCacheTimestamp);
        Assert.False(record.InputChanged);
    }

    [Fact]
    public async Task Request_attribution_flows_across_await_and_is_restored()
    {
        var journal = new IndexDiagnostics();
        using (var request = journal.BeginRequest(RequestId(17), State()))
        {
            request.Enter(IndexDiagnosticStage.Commit, State());
            await Task.Run(() => {
                request.Enter(IndexDiagnosticStage.Resolve, State());
                journal.Record(State(), State(stamp: 11), IndexDiagnosticCause.CacheFreshnessOnly, IndexDiagnosticSource.Catalog, null);
            });
            request.Cancel(State(stamp: 12));
        }
        var cancelled = journal.Snapshot().Last();
        Assert.Equal(RequestId(17), cancelled.RequestId);
        Assert.Equal(IndexDiagnosticStage.Resolve, cancelled.Stage);
        Assert.Equal(IndexDiagnosticCause.Cancelled, cancelled.Cause);
        Assert.Equal(IndexDiagnosticCancellation.Unknown, cancelled.Cancellation);
        Assert.Equal(10, cancelled.Before.PsiCacheTimestamp);
        Assert.Equal(12, cancelled.After.PsiCacheTimestamp);
        Assert.All(journal.Snapshot(), item => Assert.Equal(RequestId(17), item.RequestId));
        journal.Record(State(), State(), IndexDiagnosticCause.NoBusinessChange, IndexDiagnosticSource.DocumentChange, false);
        Assert.Null(journal.Snapshot().Last().RequestId);
    }

    [Fact]
    public async Task Concurrent_requests_are_not_cross_attributed()
    {
        var journal = new IndexDiagnostics();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(async () => {
            using var request = journal.BeginRequest(RequestId(i), State(i));
            await Task.Yield();
            request.Enter(IndexDiagnosticStage.Navigate, State(i));
            request.Complete(IndexDiagnosticStatus.NavigationDispatched, State(i));
        })));
        var replies = journal.Snapshot().Where(item => item.Stage == IndexDiagnosticStage.Reply).ToArray();
        Assert.Equal(20, replies.Length);
        Assert.All(replies, item => Assert.Equal(RequestId((int)item.After.Epoch), item.RequestId));
        Assert.Equal(60, journal.Snapshot().Select(item => item.Sequence).Distinct().Count());
    }

    [Fact]
    public async Task Explicit_attribution_handles_host_scheduler_without_execution_context_flow()
    {
        var journal = new IndexDiagnostics();
        using var request = journal.BeginRequest(RequestId(17), State());
        Task read;
        using (ExecutionContext.SuppressFlow())
            read = Task.Run(() => {
                using var attribution = request.Activate();
                request.Enter(IndexDiagnosticStage.Resolve, State());
                journal.Record(State(), State(), IndexDiagnosticCause.NoBusinessChange, IndexDiagnosticSource.Catalog, null);
            });
        await read;
        Assert.All(journal.Snapshot(), item => Assert.Equal(RequestId(17), item.RequestId));
    }

    [Theory]
    [InlineData(IndexDiagnosticStage.Commit)]
    [InlineData(IndexDiagnosticStage.Resolve)]
    [InlineData(IndexDiagnosticStage.Navigate)]
    public async Task Real_task_cancellation_retains_last_entered_phase(IndexDiagnosticStage stage)
    {
        var journal = new IndexDiagnostics();
        using var request = journal.BeginRequest("cancel-17", State());
        request.Enter(stage, State());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await Task.Delay(1, cancellation.Token); }
        catch (OperationCanceledException) { request.Cancel(State()); }
        var record = journal.Snapshot().Last();
        Assert.Equal(IndexDiagnosticCause.Cancelled, record.Cause);
        Assert.Equal(stage, record.Stage);
        Assert.Equal(IndexDiagnosticCancellation.Unknown, record.Cancellation);
        Assert.Null(record.Status);
    }

    [Fact]
    public void Journal_contains_digests_not_raw_input_or_uncontrolled_identifiers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "afsm-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "secret.cs");
            var raw = "class PrivateNode { string Credential = \"not-a-real-secret\"; }";
            File.WriteAllText(path, raw);
            var summary = IndexDiagnostics.Summarize(File.ReadAllText(path));
            var journal = new IndexDiagnostics();
            using var request = journal.BeginRequest(raw, State(summary: summary));
            journal.Record(State(summary: raw), State(summary: summary), IndexDiagnosticCause.NoBusinessChange, IndexDiagnosticSource.DocumentChange, false);
            var json = JsonSerializer.Serialize(journal.Snapshot());
            Assert.DoesNotContain("PrivateNode", json);
            Assert.DoesNotContain("Credential", json);
            Assert.DoesNotContain(directory, json);
            Assert.Contains(summary, json);
            Assert.Equal(64, journal.Snapshot().Last().Before.InputSummary!.Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Oversized_request_id_is_bounded_and_reply_is_enum_only()
    {
        var journal = new IndexDiagnostics();
        using var request = journal.BeginRequest(new string('x', 10000), State());
        request.Complete(IndexDiagnosticStatus.Unknown, State());
        var reply = journal.Snapshot().Last();
        Assert.True(reply.RequestId!.Length <= 128);
        Assert.Equal(IndexDiagnosticStatus.Unknown, reply.Status);
        Assert.Equal(IndexDiagnosticStage.Reply, reply.Stage);
        Assert.Equal(1, reply.After.Epoch);
    }

    [Fact]
    public void Non_uuid_identifiers_are_hashed_even_if_they_look_like_safe_ascii()
    {
        var journal = new IndexDiagnostics();
        using var request = journal.BeginRequest("not-a-real-credential", State());
        var record = Assert.Single(journal.Snapshot());
        Assert.Equal("sha256:" + IndexDiagnostics.Summarize("not-a-real-credential"), record.RequestId);
        Assert.DoesNotContain("not-a-real-credential", JsonSerializer.Serialize(record));
    }

    [Fact]
    public void Invalid_capacity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexDiagnostics(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexDiagnostics(4097));
    }
}
