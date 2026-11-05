using StateMachine;

namespace StateMachineTest;

public class PreparationConfigurationFacts
{
    private sealed class Probe
    {
        public int Initializations;
        public int Runs;
        public Action? OnRun;
    }

    private sealed class Node(Probe probe) : BaseFSMNode
    {
        public override void InitBeforeStart() => probe.Initializations++;
        protected override Task ExecuteAsync()
        {
            probe.OnRun?.Invoke();
            probe.Runs++;
            PublishEvent(1);
            return Task.CompletedTask;
        }
    }

    private sealed class Factory(Probe probe) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new Node(probe);
        public Type GetNodeType(string key) => typeof(Node);
        public string GetNodeFeatureName(Type type) => "Work";
    }

    private static FSMEngine Engine(Probe probe, string end = "Done")
    {
        var engine = new FSMEngine(new Factory(probe));
        engine.CreateStateMachine($"def Work(Work) {{1->{end};}} def Unused event;");
        return engine;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupRevalidatesTheGraphAfterSuccessfulConfiguration(bool eventObjects)
    {
        var probe = new Probe();
        var engine = Engine(probe);
        using var prepared = engine.Prepare();
        var installed = false;
        if (eventObjects) prepared.WithEntry("Work", new[] { engine.GetEvent("Done") }, _ => installed = true);
        else prepared.WithEntry("Work", new[] { "Done" }, _ => installed = true);
        engine.CreateNode("Work", "Added");
        Action start = () => prepared.StartAsync();
        start.Should().Throw<FSMException>().WithMessage("*changed*");
        installed.Should().BeFalse();
        probe.Initializations.Should().Be(0);
        probe.Runs.Should().Be(0);
        engine.HasUnexitedExecution.Should().BeTrue();
        prepared.Dispose();
        engine.HasUnexitedExecution.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObserverFailureRetainsExplicitConfigurationForRetry(bool eventObjects)
    {
        var probe = new Probe();
        var engine = Engine(probe);
        using var prepared = engine.Prepare();
        var attempts = 0;
        void Observe(FSMExecutor _)
        {
            if (++attempts == 1) throw new InvalidOperationException("observer setup failed");
        }
        if (eventObjects) prepared.WithEntry("Work", new[] { engine.GetEvent("Done") }, Observe);
        else prepared.WithEntry("Work", new[] { "Done" }, Observe);
        Action start = () => prepared.StartAsync();
        start.Should().Throw<FSMException>().Which.GetBaseException().Message.Should().Be("observer setup failed");
        probe.Initializations.Should().Be(0);
        probe.Runs.Should().Be(0);
        engine.HasUnexitedExecution.Should().BeTrue();
        using var executor = await prepared.StartAsync();
        try
        {
            await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            attempts.Should().Be(2);
            probe.Runs.Should().Be(1);
            executor.State.Should().Be(FSMState.Finished);
        }
        finally { await executor.StopAsync(); }
    }

    [Theory]
    [InlineData(false, "starting")]
    [InlineData(true, "starting")]
    [InlineData(false, "started")]
    [InlineData(true, "started")]
    [InlineData(false, "released")]
    [InlineData(true, "released")]
    public async Task ConfigurationRejectsStartingStartedAndReleasedPreparations(bool eventObjects, string phase)
    {
        var probe = new Probe();
        var engine = Engine(probe);
        using var prepared = engine.Prepare();
        Action configure = () =>
        {
            if (eventObjects) prepared.WithEntry("Work", new[] { engine.GetEvent("Unused") });
            else prepared.WithEntry("Work", new[] { "Unused" });
        };
        var observerCalled = false;
        prepared.WithEntry("Work", new[] { "Done" }, _ =>
        {
            observerCalled = true;
            if (phase == "starting") configure.Should().Throw<FSMException>();
        });
        if (phase == "released")
        {
            prepared.Dispose();
            configure.Should().Throw<FSMException>();
            observerCalled.Should().BeFalse();
            probe.Initializations.Should().Be(0);
            return;
        }
        using var executor = await prepared.StartAsync();
        try
        {
            if (phase == "started") configure.Should().Throw<FSMException>();
            await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            observerCalled.Should().BeTrue();
            executor.State.Should().Be(FSMState.Finished);
        }
        finally { await executor.StopAsync(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EnumerationDoesNotBlockStartupOrDisposalAndCannotOverwriteTheirSelection(bool eventObjects, bool dispose)
    {
        var probe = new Probe();
        var engine = Engine(probe);
        using var prepared = engine.Prepare();
        var installed = 0;
        prepared.WithEntry("Work", new[] { "Done" }, _ => installed++);
        var enumerating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        IEnumerable<T> Hold<T>(T value)
        {
            enumerating.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("enumeration was not released");
            yield return value;
        }
        var configuring = Task.Run(() => Record.Exception(() =>
        {
            if (eventObjects) prepared.WithEntry("Work", Hold(engine.GetEvent("Unused")));
            else prepared.WithEntry("Work", Hold("Unused"));
        }));
        Task<FSMExecutor>? starting = null;
        Task? disposing = null;
        try
        {
            await enumerating.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (dispose)
            {
                disposing = Task.Run(prepared.Dispose);
                await disposing.WaitAsync(TimeSpan.FromSeconds(5));
                engine.HasUnexitedExecution.Should().BeFalse();
            }
            else
            {
                starting = Task.Run(() => prepared.StartAsync());
                var executor = await starting.WaitAsync(TimeSpan.FromSeconds(5));
                await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
                installed.Should().Be(1);
                probe.Runs.Should().Be(1);
            }
            release.Set();
            (await configuring.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeOfType<FSMException>();
        }
        finally
        {
            release.Set();
            await configuring.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposing != null) await disposing.WaitAsync(TimeSpan.FromSeconds(5));
            if (starting != null)
            {
                using var executor = await starting.WaitAsync(TimeSpan.FromSeconds(5));
                await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationSnapshotsCollectionsAndAcceptsHostOwnedEndEvents(bool eventObjects)
    {
        var probe = new Probe();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.OnRun = () => ran.TrySetResult();
        var engine = Engine(probe);
        var payload = new object();
        var hostEnd = new FSMEvent("HostDone") { EventContext = payload };
        var names = new List<string> { "Unused", "Done" };
        var events = new List<FSMEvent> { engine.GetEvent("Unused"), hostEnd };
        using var prepared = engine.Prepare();
        if (eventObjects) prepared.WithEntry("Work", events);
        else prepared.WithEntry("Work", names);
        names.Clear();
        events.Clear();
        using var executor = await prepared.StartAsync();
        try
        {
            await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (eventObjects) executor.Execution.PublishEvent("HostDone");
            await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            probe.Runs.Should().Be(1);
            executor.State.Should().Be(FSMState.Finished);
            engine.ContainsEvent("HostDone").Should().BeFalse();
            hostEnd.EventContext.Should().BeSameAs(payload);
        }
        finally { await executor.StopAsync(); }
    }

    [Theory]
    [InlineData("missing-entry")]
    [InlineData("missing-event")]
    [InlineData("empty-names")]
    [InlineData("empty-objects")]
    [InlineData("null-names")]
    [InlineData("null-objects")]
    [InlineData("null-member")]
    [InlineData("throwing-names")]
    [InlineData("throwing-objects")]
    public async Task InvalidConfigurationPreservesThePreviousCompleteSelection(string invalid)
    {
        var probe = new Probe();
        var engine = Engine(probe);
        using var prepared = engine.Prepare();
        var installed = 0;
        prepared.WithEntry("Work", new[] { "Done" }, _ => installed++);
        Action<FSMExecutor> replacement = _ => installed += 100;
        Action configure = invalid switch
        {
            "missing-entry" => () => prepared.WithEntry("Missing", new[] { "Unused" }, replacement),
            "missing-event" => () => prepared.WithEntry("Work", new[] { "Unused", "Missing" }, replacement),
            "empty-names" => () => prepared.WithEntry("Work", Array.Empty<string>(), replacement),
            "empty-objects" => () => prepared.WithEntry("Work", Array.Empty<FSMEvent>(), replacement),
            "null-names" => () => prepared.WithEntry("Work", (IEnumerable<string>)null!, replacement),
            "null-objects" => () => prepared.WithEntry("Work", (IEnumerable<FSMEvent>)null!, replacement),
            "null-member" => () => prepared.WithEntry("Work", new FSMEvent[] { engine.GetEvent("Unused"), null! }, replacement),
            "throwing-names" => () => prepared.WithEntry("Work", ThrowAfter("Unused"), replacement),
            "throwing-objects" => () => prepared.WithEntry("Work", ThrowAfter(engine.GetEvent("Unused")), replacement),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        if (invalid.StartsWith("throwing-", StringComparison.Ordinal))
            configure.Should().Throw<InvalidOperationException>().WithMessage("enumeration failed");
        else if (invalid == "missing-event") configure.Should().Throw<KeyNotFoundException>();
        else configure.Should().Throw<FSMException>();
        installed.Should().Be(0);
        probe.Initializations.Should().Be(0);
        using var executor = await prepared.StartAsync();
        try
        {
            await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            installed.Should().Be(1);
            probe.Runs.Should().Be(1);
            executor.State.Should().Be(FSMState.Finished);
        }
        finally { await executor.StopAsync(); }
    }

    private static IEnumerable<T> ThrowAfter<T>(T first)
    {
        yield return first;
        throw new InvalidOperationException("enumeration failed");
    }

    [Theory]
    [InlineData("names")]
    [InlineData("objects")]
    [InlineData("single")]
    public async Task ReconfigurationReplacesEntryEndEventsAndObserver(string selection)
    {
        var probe = new Probe();
        var engine = new FSMEngine(new Factory(probe));
        engine.CreateStateMachine("def Old(Work) {1->OldEnd;} def First(Work) {1->Next;} def Last(Work) {1->Done;} Next -> First to Last;");
        using var prepared = engine.Prepare();
        prepared.WithEntry("Old", new[] { "OldEnd", "Next" }, _ => throw new InvalidOperationException("replaced observer"));
        switch (selection)
        {
            case "names": prepared.WithEntry("First", new[] { "Done" }); break;
            case "objects": prepared.WithEntry("First", new[] { engine.GetEvent("Done") }); break;
            case "single": prepared.WithEntry("First", "Done"); break;
        }
        using var executor = await prepared.StartAsync();
        try
        {
            await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            probe.Runs.Should().Be(2, "the old entry and early end event must both be replaced");
            executor.State.Should().Be(FSMState.Finished);
        }
        finally { await executor.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoordinatedConfigurationInstallsAllObserversBeforeAnyNodeRuns(bool eventObjects)
    {
        var a = new Probe();
        var b = new Probe();
        var ea = Engine(a, "DoneA");
        var eb = Engine(b, "DoneB");
        using var pa = ea.Prepare();
        using var pb = eb.Prepare();
        var observed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var installed = 0;
        void Observe(FSMExecutor executor)
        {
            a.Initializations.Should().Be(0);
            b.Initializations.Should().Be(0);
            installed++;
            executor.NodeStateChanged += (_, name) => observed.Enqueue(name);
        }
        a.OnRun = b.OnRun = () => installed.Should().Be(2);
        FSMContextPreparation Configure(FSMContextPreparation preparation, FSMEngine engine, string end)
            => eventObjects
                ? preparation.WithEntry("Work", new[] { engine.GetEvent("Unused"), engine.GetEvent(end) }, Observe)
                : preparation.WithEntry("Work", new[] { "Unused", end }, Observe);
        Configure(pa, ea, "DoneA").Should().BeSameAs(pa);
        Configure(pb, eb, "DoneB").Should().BeSameAs(pb);
        installed.Should().Be(0);
        a.Initializations.Should().Be(0);
        b.Initializations.Should().Be(0);
        a.Runs.Should().Be(0);
        b.Runs.Should().Be(0);

        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        try
        {
            await Task.WhenAll(executors.Select(e => e.Execution.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
            a.Runs.Should().Be(1);
            b.Runs.Should().Be(1);
            observed.Should().Equal("Work", "Work");
            executors.Should().OnlyContain(e => e.State == FSMState.Finished);
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }
}
