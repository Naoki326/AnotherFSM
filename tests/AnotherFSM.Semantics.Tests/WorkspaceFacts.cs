namespace AnotherFSM.Semantics.Tests;

public sealed class WorkspaceFacts
{
    [Fact]
    public async Task Superseded_async_analysis_cannot_publish_over_a_newer_buffer()
    {
        using var workspace = new FsmWorkspace();
        var large = string.Concat(Enumerable.Range(0, 10000).Select(i => $"def N{i}(Old) {{}} "));
        var old = workspace.UpdateAsync(new("flow.fsm", 1, large));
        var newer = await workspace.UpdateAsync(new("flow.fsm", 2, "def Current(New) {}"));
        var oldResult = await old;
        Assert.NotNull(newer);
        Assert.True(workspace.IsCurrent(newer.Stamp));
        Assert.True(oldResult == null || !workspace.IsCurrent(oldResult.Stamp));
        Assert.Equal("New", workspace.GetCurrent("flow.fsm")!.Analysis.Nodes.Single().TypeKey);
    }

    [Fact]
    public void Native_background_search_uses_the_same_version_guard_without_waiting_on_a_task()
    {
        using var workspace = new FsmWorkspace();
        var current = workspace.Update(new("native.fsm", 2, "def Current(Key) {}"))!;
        Assert.Null(workspace.Update(new("native.fsm", 1, "def Old(Key) {}")));
        Assert.Equal("Current", workspace.GetCurrent("native.fsm")!.Analysis.Nodes.Single().Name);
        Assert.True(workspace.IsCurrent(current.Stamp));
    }

    [Fact]
    public async Task Context_replacement_eviction_and_solution_close_expire_results()
    {
        var workspace = new FsmWorkspace(1);
        var first = (await workspace.UpdateAsync(new("one.fsm", 1, "def A() {}")))!;
        workspace.InvalidateContext();
        Assert.False(workspace.IsCurrent(first.Stamp));
        var refreshed = workspace.GetCurrent("one.fsm")!;
        Assert.True(workspace.IsCurrent(refreshed.Stamp));
        await workspace.UpdateAsync(new("two.fsm", 1, "def B() {}"));
        Assert.Null(workspace.GetCurrent("one.fsm"));
        workspace.Dispose();
        Assert.False(workspace.IsCurrent(refreshed.Stamp));
        Assert.Null(workspace.GetCurrent("two.fsm"));
    }

    [Fact]
    public async Task Cancellation_is_not_reported_as_an_empty_successful_analysis()
    {
        using var workspace = new FsmWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.UpdateAsync(new("one.fsm", 1, "def A() {}"), cancellation.Token));
        Assert.Null(workspace.GetCurrent("one.fsm"));
    }

    [Fact]
    public async Task Unsaved_edit_invalidates_old_results_and_cannot_be_overwritten_by_an_older_version()
    {
        using var workspace = new FsmWorkspace();
        var old = await workspace.UpdateAsync(new("flow.fsm", 1, "def Query(Old) {}"));
        var latest = await workspace.UpdateAsync(new("flow.fsm", 2, "def Query(New) {}"));
        var late = await workspace.UpdateAsync(new("flow.fsm", 1, "def Query(Old) {}"));
        Assert.NotNull(old);
        Assert.NotNull(latest);
        Assert.False(workspace.IsCurrent(old.Stamp));
        Assert.True(workspace.IsCurrent(latest.Stamp));
        Assert.Null(late);
        Assert.Equal("New", workspace.GetCurrent("flow.fsm")!.Analysis.Nodes.Single().TypeKey);
    }
}
