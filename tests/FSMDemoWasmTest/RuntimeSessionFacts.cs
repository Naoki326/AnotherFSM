using System.Text.Json;
using FSMDemo.Runtime;
using StateMachine;

namespace FSMDemoWasmTest;

// Host evidence for the shared public protocol, not a substitute for browser Release tests.
public class RuntimeSessionFacts
{
    private static async Task<JsonElement> Value(RuntimeSession session, string operation, object? payload = null)
    {
        using var result = JsonDocument.Parse(await session.InvokeAsync(operation, JsonSerializer.Serialize(payload ?? new { })));
        result.RootElement.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        return result.RootElement.GetProperty("value").Clone();
    }

    private const string SamePrefixScript = """
        module M {
            input Entry, Retry; output Done;
            def Entry(Idle) { 1->Next; }
            def Retry(Idle) { 1->Done; }
            Next -> Entry to Retry;
        }
        use M as A;
        def Start(Start) { 1->Launch; }
        def A.Observer(Idle) { 1->Observe; }
        def End(End) {}
        Launch -> Start to A.Retry;
        Observe -> A.Observer to End;
        A.Done -> End;
        """;

    private sealed class PausingNode : EnumFSMNode
    {
        protected override System.Collections.IEnumerable ExecuteEnumerable()
        {
            yield return Yield.Pause;
        }
    }

    private sealed class PausingFactory : IFSMNodeFactory
    {
        private readonly BrowserNodeFactory _browser = new();
        public IFSMNode CreateNode(string key) => key == "Pausing" ? new PausingNode() : _browser.CreateNode(key);
        public Type GetNodeType(string key) => key == "Pausing" ? typeof(PausingNode) : _browser.GetNodeType(key);
        public string GetNodeFeatureName(Type type) => type == typeof(PausingNode) ? "Pausing" : _browser.GetNodeFeatureName(type);
    }

    [Theory]
    [InlineData("Pauser")]
    [InlineData("Start")]
    public async Task SynchronousNodeRequestedPauseIsPreservedAtStartAndAfterATransition(string start)
    {
        var events = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
        await using var session = new RuntimeSession(new PausingFactory(), json =>
        {
            using var doc = JsonDocument.Parse(json);
            events.Writer.TryWrite(doc.RootElement.Clone());
        });
        await Value(session, "importScript", new { script = "def Start(Start) {1->Go;} def Pauser(Pausing) {1->Done;} Go -> Start to Pauser;" });
        await Value(session, "startExecution", new { startNodeName = start, endEventName = "Done" });
        await Until(events.Reader, e => e.TryGetProperty("state", out var state) && state.GetString() == "Paused");
        (await Value(session, "getExecutionStatus")).GetProperty("state").GetString().Should().Be("Paused");
        await Value(session, "continueExecution");
        (await Value(session, "getExecutionStatus")).GetProperty("state").GetString().Should().Be("Running");
        await Value(session, "stopExecution");
    }

    [Fact]
    public async Task AsyncExecutionCanPauseContinueStopAndRestartThroughNotifications()
    {
        var events = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
        await using var session = new RuntimeSession(new BrowserNodeFactory(), json =>
        {
            using var doc = JsonDocument.Parse(json);
            events.Writer.TryWrite(doc.RootElement.Clone());
        });
        await Value(session, "importScript", new { script = "def Wait(Idle) {1->Done;} def Done event;" });
        await Value(session, "startExecution", new { startNodeName = "Wait", endEventName = "Done" });
        await Until(events.Reader, e => e.GetProperty("type").GetString() == "NodeActivated");
        await Until(events.Reader, e => e.TryGetProperty("state", out var running) && running.GetString() == "Running");
        await Value(session, "pauseExecution").WaitAsync(TimeSpan.FromSeconds(1));
        (await Value(session, "getExecutionStatus")).GetProperty("state").GetString().Should().Be("Paused");
        await Value(session, "continueExecution");
        await Until(events.Reader, e => e.TryGetProperty("state", out var s) && s.GetString() == "Running");
        await Value(session, "stopExecution").WaitAsync(TimeSpan.FromSeconds(1));
        var status = await Value(session, "getExecutionStatus");
        status.GetProperty("state").GetString().Should().Be("Idle");
        status.GetProperty("currentNodeName").ValueKind.Should().Be(JsonValueKind.Null);
        await Value(session, "startExecution", new { startNodeName = "Wait", endEventName = "Done" });
        await Until(events.Reader, e => e.GetProperty("type").GetString() == "NodeActivated");
        await Value(session, "stopExecution").WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task StopCanCancelARestartWhileThePreviousExecutorIsRetiring()
    {
        var events = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
        await using var session = new RuntimeSession(new BrowserNodeFactory(), json =>
        {
            using var doc = JsonDocument.Parse(json);
            events.Writer.TryWrite(doc.RootElement.Clone());
        });
        await Value(session, "importScript", new { script = "def Wait(Idle) {1->Done;}" });
        await Value(session, "startExecution", new { startNodeName = "Wait", endEventName = "Done" });
        await Until(events.Reader, e => e.TryGetProperty("state", out var s) && s.GetString() == "Running");
        var restarting = session.InvokeAsync("startExecution", "{\"startNodeName\":\"Wait\",\"endEventName\":\"Done\"}");
        await Value(session, "stopExecution").WaitAsync(TimeSpan.FromSeconds(1));
        await restarting.WaitAsync(TimeSpan.FromSeconds(1));
        (await Value(session, "getExecutionStatus")).GetProperty("state").GetString().Should().Be("Idle");
    }

    [Theory]
    [InlineData("def Old(Group) {}")]
    [InlineData("def Old(Parallel) {}")]
    [InlineData("def Old(Start) { [Child->Done]; }")]
    [InlineData("module M { input Old; def Old(Parallel) {} } use M as A;")]
    public async Task LegacyImportsFailBeforeExecutionAndPreserveTheExistingGraph(string script)
    {
        var notifications = new List<string>();
        await using var session = new RuntimeSession(new BrowserNodeFactory(), notifications.Add);
        await Value(session, "importScript", new { script = SamePrefixScript });
        notifications.Clear();
        var before = (await Value(session, "exportScript")).GetString();
        using var error = JsonDocument.Parse(await session.InvokeAsync("importScript", JsonSerializer.Serialize(new { script })));
        error.RootElement.GetProperty("error").GetString().Should().Contain("Module").And.Contain("not equivalent");
        (await Value(session, "exportScript")).GetString().Should().Be(before);
        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task FlattenedModuleExecutionCanStopAndRestart()
    {
        var events = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
        await using var session = new RuntimeSession(new BrowserNodeFactory(), json =>
        {
            using var doc = JsonDocument.Parse(json);
            events.Writer.TryWrite(doc.RootElement.Clone());
        });
        await Value(session, "importScript", new { script = "module M { input Entry; output Done; def Entry(Idle) {1->Done;} } use M as A;" });
        for (var i = 0; i < 20; i++)
        {
            await Value(session, "startExecution", new { startNodeName = "A.Entry", endEventName = "A.Done" }).WaitAsync(TimeSpan.FromSeconds(2));
            await Until(events.Reader, e => e.GetProperty("type").GetString() == "NodeActivated" && e.GetProperty("nodeName").GetString() == "A.Entry");
            await Until(events.Reader, e => e.TryGetProperty("state", out var state) && state.GetString() == "Running");
            await Value(session, "stopExecution").WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData("A.Entry", "End", true)]
    [InlineData("A.Finish", "UnexpectedEnd", false)]
    public async Task ExecutionTreatsOnlyActualModuleMembersAsInternalEndNodes(string start, string target, bool entered)
    {
        var events = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();
        var activated = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using var session = new RuntimeSession(new BrowserNodeFactory(), json =>
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement.Clone();
            if (e.GetProperty("type").GetString() == "NodeActivated") activated.Enqueue(e.GetProperty("nodeName").GetString()!);
            events.Writer.TryWrite(e);
        });
        await Value(session, "importScript", new { script = """
            module M { input Entry; output Done; def Entry(End) { 1->Done; } }
            use M as A;
            def End(End) { 1->EndEvent; }
            A.Done -> End;
            def A.Finish(End) { 1->Complete; }
            def UnexpectedEnd(End) { 1->EndEvent; }
            Complete -> A.Finish to UnexpectedEnd;
            """ });
        await Value(session, "startExecution", new { startNodeName = start, endEventName = "EndEvent" });
        await Until(events.Reader, e => e.TryGetProperty("state", out var s) && s.GetString() == "Finished");
        activated.Contains(target).Should().Be(entered);
        var modules = await Value(session, "getModuleInstances");
        modules[0].GetProperty("internalNodeNames").EnumerateArray().Select(n => n.GetString()).Should().Equal("A.Entry");
    }

    [Fact]
    public async Task SuccessfulReimportStopsOldExecutionAndDisposePublishesNoMoreNotifications()
    {
        var notifications = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new RuntimeSession(new BrowserNodeFactory(), json =>
        {
            notifications.Enqueue(json);
            if (json.Contains("\"state\":\"Running\"")) running.TrySetResult();
        });
        await Value(session, "importScript", new { script = "def Wait(Idle) {1->Done;}" });
        await Value(session, "startExecution", new { startNodeName = "Wait", endEventName = "Done" });
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Value(session, "importScript", new { script = "def New(Start) {}" });
        (await Value(session, "getExecutionStatus")).GetProperty("state").GetString().Should().Be("Idle");
        await session.DisposeAsync();
        var count = notifications.Count;
        using var error = JsonDocument.Parse(await session.InvokeAsync("startExecution", "{\"startNodeName\":\"New\",\"endEventName\":\"Done\"}"));
        error.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        notifications.Count.Should().Be(count);
    }

    private static async Task<JsonElement> Until(System.Threading.Channels.ChannelReader<JsonElement> events, Func<JsonElement, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var next = await events.ReadAsync(timeout.Token);
            if (predicate(next)) return next;
        }
    }

    [Theory]
    [InlineData("import External;", "external module files are not supported")]
    [InlineData("use External as A;", "External")]
    [InlineData("def Device(UnknownDevice) {}", "UnknownDevice")]
    public async Task BrowserCapabilitiesFailExplicitlyWithoutDestroyingTheGraph(string script, string reason)
    {
        await using var session = new RuntimeSession(new BrowserNodeFactory(), _ => { });
        await Value(session, "createNode", new { type = "Start", name = "Keep", posX = 0, posY = 0 });
        using var error = JsonDocument.Parse(await session.InvokeAsync("importScript", JsonSerializer.Serialize(new { script })));
        error.RootElement.GetProperty("error").GetString().Should().ContainEquivalentOf(reason);
        (await Value(session, "getNode", new { name = "Keep" })).GetProperty("name").GetString().Should().Be("Keep");
    }

    [Fact]
    public async Task ConnectionsCanBeCreatedRenamedAndDeletedWithoutRebindingModuleOutputs()
    {
        await using var session = new RuntimeSession(new BrowserNodeFactory(), _ => { });
        await Value(session, "importScript", new { script = SamePrefixScript });
        await Value(session, "deleteConnection", new { fromNodeName = "Start", toNodeName = "A.Retry", eventName = "Launch" });
        using (var error = JsonDocument.Parse(await session.InvokeAsync("createConnection", "{\"fromNodeName\":\"Start\",\"toNodeName\":\"A\",\"eventName\":\"Launch\"}")))
            error.RootElement.GetProperty("error").GetString().Should().Contain("input");
        await Value(session, "createConnection", new { fromNodeName = "Start", toNodeName = "End", eventName = "Launch" });
        await Value(session, "renameConnection", new { fromNodeName = "Start", toNodeName = "End", eventName = "Launch", newEventName = "Other" });
        (await Value(session, "getConnections")).EnumerateArray().Should().Contain(e => e.GetProperty("eventName").GetString() == "Other");
        await Value(session, "deleteConnection", new { fromNodeName = "Start", toNodeName = "End", eventName = "Other" });
        using var missing = JsonDocument.Parse(await session.InvokeAsync("deleteConnection", "{\"fromNodeName\":\"Start\",\"toNodeName\":\"End\"}"));
        missing.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        var retry = await Value(session, "getNode", new { name = "A.Retry" });
        retry.GetProperty("eventDescriptions")[0].GetProperty("description").GetString().Should().Be("A.Done");
    }

    [Fact]
    public async Task ModuleMembershipAndPublicConnectionsSurviveExportAndFailedImport()
    {
        var notifications = new List<string>();
        await using var session = new RuntimeSession(new BrowserNodeFactory(), notifications.Add);
        await Value(session, "importScript", new { script = SamePrefixScript });
        notifications.Should().ContainSingle(e => e == "{\"type\":\"EngineImported\"}");
        var modules = await Value(session, "getModuleInstances");
        modules[0].GetProperty("internalNodeNames").EnumerateArray().Select(n => n.GetString()).Should().BeEquivalentTo("A.Entry", "A.Retry");
        var edges = await Value(session, "getConnections");
        edges.EnumerateArray().Should().Contain(e => e.GetProperty("fromNodeName").GetString() == "A.Observer" && e.GetProperty("toNodeName").GetString() == "End");
        edges.EnumerateArray().Should().Contain(e => e.GetProperty("fromNodeName").GetString() == "A" && e.GetProperty("eventName").GetString() == "A.Done");
        var exported = (await Value(session, "exportScript")).GetString();
        await Value(session, "importScript", new { script = exported });
        var before = (await Value(session, "getStatus")).ToString();
        using var error = JsonDocument.Parse(await session.InvokeAsync("importScript", "{\"script\":\"def Bad(Unknown) {}\"}"));
        error.RootElement.GetProperty("error").GetString().Should().Contain("Unknown");
        (await Value(session, "getStatus")).ToString().Should().Be(before);
        (await Value(session, "getModuleDefinitions"))[0].GetProperty("inputs").GetArrayLength().Should().Be(2);
        var b = await Value(session, "createModuleInstance", new { moduleName = "M", instanceName = "B", posX = 1, posY = 2 });
        b.GetProperty("instanceName").GetString().Should().Be("B");
        await Value(session, "updateModuleInstancePosition", new { instanceName = "B", posX = 5, posY = 6 });
        (await Value(session, "getModuleInstances"))[1].GetProperty("posX").GetDouble().Should().Be(5);
        await Value(session, "deleteModuleInstance", new { instanceName = "B" });
        (await Value(session, "getModuleInstances")).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task RemovedCompositeOperationReportsMigrationWithoutChangingTheGraph()
    {
        await using var session = new RuntimeSession(new BrowserNodeFactory(), _ => { });
        await Value(session, "importScript", new { script = SamePrefixScript });
        var before = (await Value(session, "exportScript")).GetString();
        using var error = JsonDocument.Parse(await session.InvokeAsync("updateGroupDefs", "{\"name\":\"A.Entry\",\"groupDefs\":[]}"));
        error.RootElement.GetProperty("error").GetString().Should().Contain("Module").And.Contain("not equivalent");
        (await Value(session, "exportScript")).GetString().Should().Be(before);
    }

    [Fact]
    public async Task OrdinaryNodeEditsAreObservableAndFailedEditsAreErrors()
    {
        await using var session = new RuntimeSession(new BrowserNodeFactory(), _ => { });
        await Value(session, "createNode", new { type = "Sleep", name = "G", posX = 0, posY = 0 });
        (await Value(session, "renameNode", new { name = "G", newName = "Wrapper" })).ValueKind.Should().Be(JsonValueKind.Null);
        await Value(session, "updateNodePosition", new { name = "Wrapper", posX = 42, posY = 24 });
        await Value(session, "updateNodeEvent", new { name = "Wrapper", index = 1, newEventName = "Done" });
        var nodes = await Value(session, "getNodes");
        nodes.GetArrayLength().Should().Be(1);
        var node = nodes[0];
        node.GetProperty("posY").GetDouble().Should().Be(24);
        node.TryGetProperty("groupDefs", out _).Should().BeFalse();
        node.GetProperty("eventDescriptions").EnumerateArray().Should().Contain(e => e.GetProperty("description").GetString() == "Done");
        await Value(session, "deleteNode", new { name = "Wrapper" });
        (await Value(session, "getStatus")).GetProperty("nodeNames").GetArrayLength().Should().Be(0);
        foreach (var operation in new[] { "getNode", "deleteNode", "renameNode", "updateNodePosition", "updateNodeEvent", "updateGroupDefs" })
        {
            using var error = JsonDocument.Parse(await session.InvokeAsync(operation,
                "{\"name\":\"Missing\",\"newName\":\"X\",\"posX\":1,\"posY\":2,\"index\":1,\"newEventName\":\"X\",\"groupDefs\":[]}"));
            error.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("{\"type\":\"Group\",\"name\":\"Old\",\"posX\":0,\"posY\":0}")]
    [InlineData("{\"type\":\"Parallel\",\"name\":\"Old\",\"posX\":0,\"posY\":0}")]
    [InlineData("{\"type\":\"Sleep\",\"name\":\"Old\",\"posX\":0,\"posY\":0,\"groupDefs\":[]}")]
    public async Task LegacyCreationRequestsFailWithMigrationInsteadOfIgnoringConfiguration(string payload)
    {
        await using var session = new RuntimeSession(new BrowserNodeFactory(), _ => { });
        using var reply = JsonDocument.Parse(await session.InvokeAsync("createNode", payload));
        reply.RootElement.GetProperty("error").GetString().Should().Contain("Module").And.Contain("not equivalent");
        (await Value(session, "getNodes")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CreatedNodeIsAvailableThroughThePublicQueryContract()
    {
        await using var session = new RuntimeSession(new BrowserNodeFactory(), _ => { });
        var created = await Value(session, "createNode", new { type = "Sleep", name = "Wait", posX = 12, posY = 34 });
        created.GetProperty("name").GetString().Should().Be("Wait");
        var node = await Value(session, "getNode", new { name = "Wait" });
        node.GetProperty("posX").GetDouble().Should().Be(12);
        node.GetProperty("classType").GetString().Should().Be("Sleep");
    }
}
