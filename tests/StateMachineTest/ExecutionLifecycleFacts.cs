using StateMachine;

namespace StateMachineTest;

public class ExecutionLifecycleFacts
{
    public sealed class Input { }
    private sealed class Idle : BaseFSMNode
    {
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }
    private sealed class Factory(Func<IFSMNode>? create = null, Type? declared = null) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => create?.Invoke() ?? new Idle();
        public Type GetNodeType(string key) => declared ?? typeof(Idle);
        public string GetNodeFeatureName(Type type) => "Idle";
    }
    private static FSMEngine Engine()
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateNode("Idle", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        return engine;
    }

    public sealed class Gate : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ThrowOnCancel { get; init; }
    }
    public sealed class GateInput { public Gate Gate { get; init; } = new(); }
    private sealed class Gated : BaseFSMNode<Gate>
    {
        protected override async Task ExecuteAsync()
        {
            using var registration = Control.Token.Register(() =>
            {
                Context.Canceled.TrySetResult();
                if (Context.ThrowOnCancel) throw new InvalidOperationException("cancel probe");
            });
            Context.Entered.TrySetResult();
            await Context.Release.Task;
            PublishEvent(1);
        }
    }
    private sealed class GateFactory : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new Gated();
        public Type GetNodeType(string key) => typeof(Gated);
        public string GetNodeFeatureName(Type type) => "Gated";
    }
    private static FSMEngine GateEngine(FSMEvent? done = null)
    {
        var engine = new FSMEngine(new GateFactory());
        engine.CreateNode("Gated", "Start");
        engine.AddEvent(done ?? new FSMEvent("Done"));
        engine.AttachEvent("Start", "Done", 1);
        return engine;
    }

    private sealed class LateScenario
    {
        public bool PauseOnly { get; init; }
        public TaskCompletionSource CurrentRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class LateNode(LateScenario scenario) : BaseFSMNode
    {
        private int calls;
        protected override async Task ExecuteAsync()
        {
            if (++calls == 1) _ = LateAsync();
            else await scenario.CurrentRelease.Task;
        }
        private async Task LateAsync()
        {
            await scenario.Release.Task;
            if (scenario.PauseOnly) Pause(); else PublishEvent(1);
            scenario.Sent.TrySetResult();
        }
    }

    public sealed class BlockingInput
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Gate Gate
        {
            get
            {
                Reading.TrySetResult();
                Release.Task.GetAwaiter().GetResult();
                return new Gate();
            }
        }
    }

    [Fact]
    public async Task ConcurrentPrepareCompetitorFailsWhileTheWinnerIsResolvingSources()
    {
        var flow = GateEngine().BindContext<BlockingInput>("Start", "Done");
        var winnerInput = new BlockingInput();
        var winner = Task.Run(() => flow.Prepare(winnerInput));
        await winnerInput.Reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var loserInput = new BlockingInput();
        try
        {
            Func<Task> competitor = async () => await Task.Run(() => flow.Prepare(loserInput)).WaitAsync(TimeSpan.FromSeconds(5));
            await competitor.Should().ThrowAsync<FSMException>();
            loserInput.Reading.Task.IsCompleted.Should().BeFalse();
        }
        finally { winnerInput.Release.TrySetResult(); loserInput.Release.TrySetResult(); }
        using var preparation = await winner.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public sealed class CountedInput
    {
        public int Reads;
        public Gate Gate { get { Interlocked.Increment(ref Reads); return new Gate(); } }
    }

    [Fact]
    public void ConflictingPrepareRejectsBeforeEvaluatingAnotherRoundsSources()
    {
        var engine = GateEngine();
        using var first = engine.BindContext<GateInput>("Start", "Done").Prepare(new GateInput());
        var input = new CountedInput();
        var flow = engine.BindContext<CountedInput>("Start", "Done");
        Action conflict = () => flow.Prepare(input);
        conflict.Should().Throw<FSMException>();
        input.Reads.Should().Be(0);
    }

    [Fact]
    public async Task DisposingAnExitedExecutorCannotCancelNodesBorrowedByTheNextExecution()
    {
        var engine = GateEngine();
        var flow = engine.BindContext<GateInput>("Start", "Done");
        var a = new GateInput();
        var first = await flow.Prepare(a).StartAsync();
        await a.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        a.Gate.Release.TrySetResult();
        await first.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        var b = new GateInput();
        using var second = await flow.Prepare(b).StartAsync();
        await b.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            first.Dispose();
            b.Gate.Canceled.Task.IsCompleted.Should().BeFalse();
            second.State.Should().Be(FSMState.Running);
        }
        finally { b.Gate.Release.TrySetResult(); await second.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateNodeNotificationKeepsItsOriginalRoundAcrossLegacyRestart(bool pause)
    {
        var scenario = new LateScenario { PauseOnly = pause };
        var node = new LateNode(scenario);
        var engine = new FSMEngine(new Factory(() => node));
        engine.CreateNode("Idle", "Start");
        engine.AttachEvent("Start", "Done", 1);
        using var executor = new FSMExecutor(node, engine.GetEvent("Done"));
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        try
        {
            scenario.Release.TrySetResult();
            await scenario.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            executor.State.Should().Be(FSMState.Running);
        }
        finally { scenario.CurrentRelease.TrySetResult(); await executor.StopAsync(); }
    }

    public sealed class CleanupProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class CleanupInput { public CleanupProbe Probe { get; init; } = new(); }
    private sealed class AsyncCleanup : AsyncEnumFSMNode<CleanupProbe>
    {
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            try { yield return Yield.Pause; }
            finally
            {
                Context.Entered.TrySetResult();
                await Context.Release.Task;
            }
        }
    }

    private sealed class SyncCleanup : EnumFSMNode<CleanupProbe>
    {
        protected override System.Collections.IEnumerable ExecuteEnumerable()
        {
            try { yield return Yield.Pause; }
            finally
            {
                Context.Entered.TrySetResult();
                Context.Release.Task.GetAwaiter().GetResult();
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopCompletionWaitsForAsyncIteratorCleanupAndKeepsEngineOccupied(bool asyncCleanup)
    {
        var engine = new FSMEngine(new Factory(() => asyncCleanup ? new AsyncCleanup() : new SyncCleanup(), asyncCleanup ? typeof(AsyncCleanup) : typeof(SyncCleanup)));
        engine.CreateNode("Cleanup", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        var flow = engine.BindContext<CleanupInput>("Start", "Done");
        var input = new CleanupInput();
        using var executor = await flow.Prepare(input).StartAsync();
        await WaitForState(executor, FSMState.Paused);
        var stopping = executor.StopAsync();
        try
        {
            await input.Probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping.IsCompleted.Should().BeFalse();
            Action conflict = () => flow.Prepare(new CleanupInput());
            conflict.Should().Throw<FSMException>();
        }
        finally { input.Probe.Release.TrySetResult(); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        using var next = flow.Prepare(new CleanupInput());
    }

    private sealed class RestartProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FSMNodeContext? Original;
        public FSMNodeContext? AtExit;
        public int Calls;
    }
    private sealed class LegacyWait(RestartProbe probe) : BaseFSMNode
    {
        protected override async Task ExecuteAsync()
        {
            if (Interlocked.Increment(ref probe.Calls) != 1) return;
            probe.Original = Context;
            using var registration = Control.Token.Register(() => probe.Canceled.TrySetResult());
            probe.Entered.TrySetResult();
            await probe.Release.Task;
            probe.AtExit = Context;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EveryRetainedRestartWaitsBeforeChangingContextAndNeverImplicitlyInheritsData(int overload)
    {
        var probe = new RestartProbe();
        var engine = new FSMEngine(new Factory(() => new LegacyWait(probe), typeof(LegacyWait)));
        engine.CreateNode("Wait", "Start");
        engine.CreateNode("Wait", "Other");
        engine.AddEvent(new FSMEvent("Done"));
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        var firstData = new object();
        var nextData = new object();
        await executor.RestartAsync(firstData);
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var restarting = overload switch
        {
            0 => executor.RestartAsync(),
            1 => executor.RestartAsync(nextData),
            2 => executor.RestartAsync(engine.GetNode("Other")),
            _ => executor.RestartAsync(engine.GetNode("Other"), nextData)
        };
        await probe.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            restarting.IsCompleted.Should().BeFalse();
            engine.GetNode("Start").Context.Should().BeSameAs(probe.Original);
            probe.Original!.Data.Should().BeSameAs(firstData);
            Action conflict = () => engine.BindContext<Input>().Prepare(new Input());
            conflict.Should().Throw<FSMException>();
        }
        finally { probe.Release.TrySetResult(); }
        (await restarting.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await WaitForState(executor, FSMState.Running);
        probe.AtExit.Should().BeSameAs(probe.Original);
        var target = engine.GetNode(overload < 2 ? "Start" : "Other");
        target.Context.Data.Should().BeSameAs(overload % 2 == 1 ? nextData : null);
        target.Context.TriggerEvent.Should().BeNull();
        await executor.StopAsync();
    }

    [Fact]
    public async Task CompetingLegacyStartsOnlyLetOneExecutionBorrowTheEngine()
    {
        var probe = new RestartProbe();
        var engine = new FSMEngine(new Factory(() => new LegacyWait(probe), typeof(LegacyWait)));
        engine.CreateNode("Wait", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        using var first = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        using var second = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt(FSMExecutor executor, object data)
        {
            await go.Task;
            try { return await executor.RestartAsync(data); }
            catch (FSMException) { return false; }
        }
        var a = Task.Run(() => Attempt(first, new object()));
        var b = Task.Run(() => Attempt(second, new object()));
        go.TrySetResult();
        var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            results.Count(success => success).Should().Be(1);
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            probe.Calls.Should().Be(1);
            engine.GetNode("Start").Context.Should().BeSameAs(probe.Original);
        }
        finally
        {
            probe.Release.TrySetResult();
            if (results[0]) await first.StopAsync();
            if (results[1]) await second.StopAsync();
        }
    }

    private sealed class CountVisits(Action visit) : BaseFSMNode
    {
        protected override Task ExecuteAsync() { visit(); return Task.CompletedTask; }
    }

    [Fact]
    public async Task AContinueInterruptedByStopCannotResumeTheNextLegacyRound()
    {
        var visits = 0;
        var hits = new List<string>();
        var nodes = new Queue<IFSMNode>(new IFSMNode[] { new CountVisits(() => visits++), new CountingEnd(hits, "done") });
        var engine = new FSMEngine(new Factory(() => nodes.Dequeue()));
        engine.CreateNode("Counter", "Start");
        engine.CreateNode("End", "Target");
        engine.AttachEvent("Target", "Done", 1);
        engine.ConnectNode("Go", "Start", "Target");
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        await executor.PauseAsync();
        executor.FSMStateChanged += (_, state, _) => { if (state == FSMState.Proceeding) executor.Stop(); };
        executor.Continue().Should().BeTrue();
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        executor.Execution.PublishEvent("Go");
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        visits.Should().Be(2);
        hits.Should().Equal("done");
    }

    [Fact]
    public async Task BusinessEndConcurrentWithContinueCannotRunAFinishedNodeAgain()
    {
        var visits = 0;
        var engine = new FSMEngine(new Factory(() => new CountVisits(() => visits++), typeof(CountVisits)));
        engine.CreateNode("Counter", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        using var executor = await engine.BindContext<Input>("Start", "Done").Prepare(new Input()).StartAsync();
        await WaitForState(executor, FSMState.Running);
        await executor.Execution.PauseAsync();
        executor.FSMStateChanged += (_, state, _) =>
        {
            if (state == FSMState.Proceeding) executor.Execution.PublishEvent("Done");
        };
        executor.Execution.Continue().Should().BeTrue();
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        visits.Should().Be(1);
        executor.State.Should().Be(FSMState.Finished);
    }

    private sealed class InitializingIdle : BaseFSMNode
    {
        public int Initializations { get; private set; }
        public override void InitBeforeStart() => Initializations++;
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task DirectNodeRunCannotBypassPreparedOwnershipOrExecutorRouting()
    {
        var visits = 0;
        var engine = new FSMEngine(new Factory(() => new CountVisits(() => visits++), typeof(CountVisits)));
        engine.CreateNode("Counter", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        using var prepared = engine.BindContext<Input>("Start", "Done").Prepare(new Input());
        Func<Task> bypassPreparation = () => engine.GetNode("Start").CreateNewAsync();
        await bypassPreparation.Should().ThrowAsync<FSMException>();
        Func<Task> bypass = async () => await engine.GetNode("Start").RunAsync();
        await bypass.Should().ThrowAsync<FSMException>();
        visits.Should().Be(0);
        using var executor = await prepared.StartAsync();
        await WaitForState(executor, FSMState.Running);
        executor.Execution.PublishEvent("Done");
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        visits.Should().Be(1);
    }

    [Fact]
    public async Task StopAtTheNewLegacyInitializedNotificationTargetsTheNewRound()
    {
        var visits = 0;
        var engine = new FSMEngine(new Factory(() => new CountVisits(() => visits++), typeof(CountVisits)));
        engine.CreateNode("Counter", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        await executor.StopAsync();
        executor.FSMStateChanged += (_, state, _) => { if (state == FSMState.Initialized) executor.Stop(); };
        await executor.RestartAsync();
        try { executor.State.Should().BeOneOf(FSMState.Stopping, FSMState.Stoped); }
        finally { await executor.StopAsync(); }
        visits.Should().Be(1);
    }

    [Fact]
    public async Task LegacyLoopInitializesNodePrivateStateOncePerNewExecution()
    {
        var node = new InitializingIdle();
        var engine = new FSMEngine(new Factory(() => node, typeof(InitializingIdle)));
        engine.CreateNode("Idle", "Start");
        engine.ConnectNode("Again", "Start", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        using var executor = new FSMExecutor(node, engine.GetEvent("Done"));
        await executor.RestartAsync();
        node.Initializations.Should().Be(1);
        await executor.RestartAsync();
        node.Initializations.Should().Be(2);
        await executor.StopAsync();
    }

    private sealed class ImmediateEnd : BaseFSMNode
    {
        protected override Task ExecuteAsync() { PublishEvent(1); return Task.CompletedTask; }
    }

    [Fact]
    public async Task FinishedAndWaitCurrentTaskDoNotReleaseOwnershipBeforeTheNodeCallbackReturns()
    {
        var node = new ImmediateEnd();
        var engine = new FSMEngine(new Factory(() => node, typeof(ImmediateEnd)));
        engine.CreateNode("End", "Start");
        engine.AttachEvent("Start", "Done", 1);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        ((IFSMNode)node).ContinueWith += _ =>
        {
            callbackEntered.TrySetResult();
            release.Wait(); // public node callback held at an explicit synchronization point
        };
        var flow = engine.BindContext<Input>("Start", "Done");
        using var executor = await flow.Prepare(new Input()).StartAsync();
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            executor.State.Should().Be(FSMState.Finished);
            await executor.CurrentNodeTask.WaitAsync(TimeSpan.FromSeconds(5));
            executor.Execution.Completion.IsCompleted.Should().BeFalse();
            Action conflict = () => flow.Prepare(new Input());
            conflict.Should().Throw<FSMException>();
        }
        finally { release.Set(); }
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        using var next = flow.Prepare(new Input());
    }

    private sealed class Interceptor(Action intercept) : IFSMEventInterceptor
    {
        public FSMEvent EventEntry(FSMEvent value) { intercept(); return value; }
    }

    [Fact]
    public async Task StopDuringBusinessInterceptionCannotEnterAnotherNode()
    {
        var engine = Engine();
        engine.CreateNode("Idle", "Target");
        engine.ConnectNode("Go", "Start", "Target");
        using var execution = await engine.BindContext<Input>("Start", "Done").Prepare(new Input()).StartAsync();
        var activated = new List<string>();
        execution.NodeStateChanged += (_, name) => activated.Add(name);
        execution.EventInterceptor = new Interceptor(execution.Stop);
        await WaitForState(execution, FSMState.Running);
        execution.Execution.PublishEvent("Go");
        await execution.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        activated.Should().NotContain("Target");
        execution.State.Should().Be(FSMState.Stoped);
    }

    [Fact]
    public async Task ExitedExecutorLegacyEventAndPauseEntriesRejectActiveCalls()
    {
        var engine = Engine();
        using var executor = await engine.BindContext<Input>("Start", "Done").Prepare(new Input()).StartAsync();
        await WaitForState(executor, FSMState.Running);
        executor.Execution.PublishEvent("Done");
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Action publish = () => executor.Handle(engine.GetEvent("Done"));
        Action pause = executor.Pause;
        publish.Should().Throw<FSMException>();
        pause.Should().Throw<FSMException>();
    }

    [Fact]
    public async Task ConcreteHandleCannotOperateTheNextLegacyExecution()
    {
        var engine = Engine();
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        var old = executor.Execution;
        await old.StopAsync();
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        Action publish = () => old.PublishEvent("Done");
        Action stop = () => old.Stop();
        Action pause = () => old.Pause();
        Action resume = () => old.Continue();
        publish.Should().Throw<FSMException>();
        stop.Should().Throw<FSMException>();
        pause.Should().Throw<FSMException>();
        resume.Should().Throw<FSMException>();
        executor.State.Should().Be(FSMState.Running);
        executor.Execution.Should().NotBeSameAs(old);
        executor.Execution.PublishEvent("Done");
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OccupancyFailureIdentifiesTheEngineExecutionAndCurrentState()
    {
        var flow = Engine().BindContext<Input>("Start", "Done");
        using var executor = await flow.Prepare(new Input()).StartAsync();
        await WaitForState(executor, FSMState.Running);
        await executor.Execution.PauseAsync();
        Action conflict = () => flow.Prepare(new Input());
        conflict.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Engine").And.Contain("execution").And.Contain("Paused").And.Contain("Prepare/start");
        await executor.StopAsync();
    }

    [Fact]
    public async Task PausedBusinessEndIsQueuedUntilContinue()
    {
        var engine = Engine();
        using var execution = await engine.BindContext<Input>("Start", "Done").Prepare(new Input()).StartAsync();
        await WaitForState(execution, FSMState.Running);
        (await execution.Execution.PauseAsync()).Should().BeTrue();
        execution.Execution.PublishEvent("Done");
        execution.State.Should().Be(FSMState.Paused);
        execution.ExecutorTask.IsCompleted.Should().BeFalse();
        execution.Continue().Should().BeTrue();
        await execution.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        execution.State.Should().Be(FSMState.Finished);
    }

    private static async Task WaitForState(FSMExecutor execution, FSMState expected)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(FSMExecutor _, FSMState state, FSMState previous) { if (state == expected) reached.TrySetResult(); }
        execution.FSMStateChanged += Changed;
        try
        {
            if (execution.State == expected) reached.TrySetResult();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { execution.FSMStateChanged -= Changed; }
    }

    [Fact]
    public async Task ContinueAndLateEndCannotOverrideStopping()
    {
        var engine = GateEngine();
        var input = new GateInput();
        using var execution = await engine.BindContext<GateInput>("Start", "Done").Prepare(input).StartAsync();
        await input.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = execution.StopAsync();
        await input.Gate.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            execution.Continue().Should().BeFalse();
            Action lateExternalEnd = () => execution.Handle(engine.GetEvent("Done"));
            lateExternalEnd.Should().Throw<FSMException>();
            execution.State.Should().Be(FSMState.Stopping);
        }
        finally { input.Gate.Release.TrySetResult(); await stopping; }
        execution.Continue().Should().BeFalse();
        execution.State.Should().Be(FSMState.Stoped);
    }

    [Fact]
    public async Task DisposeAndCallerTimeoutAreNotExitAndNeverDisposeBorrowedResources()
    {
        var engine = GateEngine();
        var flow = engine.BindContext<GateInput>("Start", "Done");
        var input = new GateInput();
        var executor = await flow.Prepare(input).StartAsync();
        await input.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        executor.Dispose();
        await input.Gate.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Func<Task> timeout = () => executor.Execution.Completion.WaitAsync(TimeSpan.Zero);
            await timeout.Should().ThrowAsync<TimeoutException>();
            Action conflict = () => flow.Prepare(new GateInput());
            conflict.Should().Throw<FSMException>();
            executor.Execution.Completion.IsCompleted.Should().BeFalse();
            executor.State.Should().Be(FSMState.Stopping);
            input.Gate.Disposed.Should().BeFalse();
        }
        finally { input.Gate.Release.TrySetResult(); }
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        input.Gate.Disposed.Should().BeFalse();
        using var next = flow.Prepare(new GateInput());
    }

    [Fact]
    public async Task CancellationCallbackFailureRemainsObservableButDoesNotReleaseANonCooperativeNode()
    {
        var engine = GateEngine();
        var flow = engine.BindContext<GateInput>("Start", "Done");
        var input = new GateInput { Gate = new Gate { ThrowOnCancel = true } };
        using var execution = await flow.Prepare(input).StartAsync();
        await input.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = execution.StopAsync();
        await input.Gate.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            stopping.IsCompleted.Should().BeFalse();
            execution.ExecutorTask.IsCompleted.Should().BeFalse();
            execution.State.Should().Be(FSMState.Stopping);
            Action prepare = () => flow.Prepare(new GateInput());
            prepare.Should().Throw<FSMException>();
        }
        finally { input.Gate.Release.TrySetResult(); }
        Func<Task> stopped = async () => await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await stopped.Should().ThrowAsync<Exception>().Where(e => e.ToString().Contains("cancel probe"));
        using var next = flow.Prepare(new GateInput());
    }

    [Fact]
    public async Task PauseContinueAndStopOnlyControlTheirTargetExecution()
    {
        var aInput = new GateInput();
        var bInput = new GateInput();
        using var a = await GateEngine().BindContext<GateInput>("Start", "Done").Prepare(aInput).StartAsync();
        using var b = await GateEngine().BindContext<GateInput>("Start", "Done").Prepare(bInput).StartAsync();
        await Task.WhenAll(aInput.Gate.Entered.Task, bInput.Gate.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            a.Execution.Pause();
            await aInput.Gate.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            bInput.Gate.Canceled.Task.IsCompleted.Should().BeFalse();
            b.State.Should().Be(FSMState.Running);
            a.Execution.Continue().Should().BeTrue();
            a.Execution.Stop();
            a.State.Should().Be(FSMState.Stopping);
            bInput.Gate.Canceled.Task.IsCompleted.Should().BeFalse();
            b.State.Should().Be(FSMState.Running);
        }
        finally
        {
            aInput.Gate.Release.TrySetResult();
            bInput.Gate.Release.TrySetResult();
            await Task.WhenAll(a.StopAsync(), b.StopAsync());
        }
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("clone")]
    [InlineData("name")]
    public async Task ConcurrentEnginesWithTheSameEventIdentityDoNotFinishEachOther(string identity)
    {
        var done = new FSMEvent("Done");
        var a = new GateInput();
        var b = new GateInput();
        using var first = await GateEngine(done).BindContext<GateInput>("Start", "Done").Prepare(a).StartAsync();
        using var second = await GateEngine(identity == "shared" ? done : identity == "clone" ? (FSMEvent)done.Clone() : new FSMEvent("Done")).BindContext<GateInput>("Start", "Done").Prepare(b).StartAsync();
        await Task.WhenAll(a.Gate.Entered.Task, b.Gate.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            a.Gate.Release.SetResult();
            await first.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
            second.State.Should().Be(FSMState.Running);
            second.ExecutorTask.IsCompleted.Should().BeFalse();
        }
        finally { b.Gate.Release.TrySetResult(); await second.StopAsync(); }
    }

    private sealed class CountingEnd(List<string> hits, string name) : BaseFSMNode
    {
        protected override Task ExecuteAsync() { hits.Add(name); PublishEvent(1); return Task.CompletedTask; }
    }
    private static FSMEngine RoutedEngine(FSMEvent go, List<string> hits)
    {
        var nodes = new Queue<IFSMNode>(new IFSMNode[] { new Idle(), new CountingEnd(hits, "business"), new CountingEnd(hits, "marker") });
        var engine = new FSMEngine(new Factory(() => nodes.Dequeue()));
        engine.CreateNode("Idle", "Start");
        engine.CreateNode("End", "Target");
        engine.CreateNode("End", "Marker");
        engine.AddEvent(go);
        engine.ConnectNode("Go", "Start", "Target");
        engine.ConnectNode("Mark", "Start", "Marker");
        engine.AttachEvent("Target", "Done", 1);
        engine.AttachEvent("Marker", "Done", 1);
        return engine;
    }

    [Fact]
    public async Task QueuedBusinessEventsAreNotCarriedAcrossAStoppedLegacyRound()
    {
        var hits = new List<string>();
        var engine = RoutedEngine(new FSMEvent("Go"), hits);
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        await executor.Execution.PauseAsync();
        executor.Execution.PublishEvent("Go");
        await executor.Execution.StopAsync();
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        executor.Execution.PublishEvent("Mark");
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        hits.Should().Equal("marker");
    }

    [Fact]
    public async Task PausedOrdinaryAndEndEventsKeepOrderAndPayloadUntilContinue()
    {
        var hits = new List<string>();
        var engine = RoutedEngine(new FSMEvent("Go"), hits);
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        await executor.RestartAsync();
        await WaitForState(executor, FSMState.Running);
        (await executor.Execution.PauseAsync()).Should().BeTrue();
        executor.Execution.PublishEvent("Go", "queued payload");
        executor.Execution.PublishEvent("Done");
        executor.State.Should().Be(FSMState.Paused);
        hits.Should().BeEmpty();
        executor.Execution.Continue().Should().BeTrue();
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        hits.Should().Equal("business");
        var target = (AbstractFSMNode)engine.GetNode("Target");
        target.TriggerEvent!.EventName.Should().Be("Go");
        target.TriggerEvent.EventContext.Should().Be("queued payload");
    }

    [Fact]
    public async Task SameEventIdentityDoesNotAuthorizeBusinessTransitionsInAnUnconnectedEngine()
    {
        var go = new FSMEvent("Go");
        var aHits = new List<string>();
        var bHits = new List<string>();
        var aEngine = RoutedEngine(go, aHits);
        var bEngine = RoutedEngine((FSMEvent)go.Clone(), bHits);
        using var a = new FSMExecutor(aEngine.GetNode("Start"), aEngine.GetEvent("Done"));
        using var b = new FSMExecutor(bEngine.GetNode("Start"), bEngine.GetEvent("Done"));
        await Task.WhenAll(a.RestartAsync(), b.RestartAsync());
        await Task.WhenAll(WaitForState(a, FSMState.Running), WaitForState(b, FSMState.Running));
        a.Execution.PublishEvent("Go");
        await a.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        b.Execution.PublishEvent("Mark"); // FIFO marker: any unauthorized Go would have been consumed first.
        await b.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        aHits.Should().Equal("business");
        bHits.Should().Equal("marker");
    }

    [Fact]
    public void TypedAssemblyRejectsATransitionIntoAnotherEnginesNodeInstance()
    {
        var first = Engine();
        var second = Engine();
        second.ChangeNodeName("Start", "ForeignTarget");
        first.GetNode("Start").AddTransition(new FSMEvent("Go"), second.GetNode("ForeignTarget"));
        Action assemble = () => first.BindContext<Input>("Start", "Done");
        assemble.Should().Throw<FSMException>().WithMessage("*ForeignTarget*owner*");
    }

    [Fact]
    public async Task LegacyAlternateGraphCannotRebindANodeBorrowedByAnotherEngine()
    {
        var probe = new RestartProbe();
        var owner = new FSMEngine(new Factory(() => new LegacyWait(probe), typeof(LegacyWait)));
        owner.CreateNode("Wait", "Borrowed");
        owner.AddEvent(new FSMEvent("Done"));
        using var active = new FSMExecutor(owner.GetNode("Borrowed"), owner.GetEvent("Done"));
        await active.RestartAsync(new object());
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var engine = Engine();
        engine.CreateNode("Idle", "Alternate");
        using var conflicting = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        engine.GetNode("Alternate").AddTransition(new FSMEvent("Go"), owner.GetNode("Borrowed"));
        try
        {
            Func<Task> restart = async () => await conflicting.RestartAsync(engine.GetNode("Alternate"));
            await restart.Should().ThrowAsync<FSMException>().WithMessage("*Borrowed*owner*");
            owner.GetNode("Borrowed").Context.Should().BeSameAs(probe.Original);
        }
        finally { probe.Release.TrySetResult(); await active.StopAsync(); }
    }

    [Fact]
    public async Task LegacyAlternateEntryCannotBypassARequiredBusinessBinding()
    {
        var node = new Gated();
        var idle = new Idle();
        var nodes = new Queue<IFSMNode>(new IFSMNode[] { idle, node });
        var engine = new FSMEngine(new Factory(() => nodes.Dequeue()));
        engine.CreateNode("Idle", "Start");
        engine.CreateNode("Gated", "Typed");
        using var legacy = new FSMExecutor(idle, new FSMEvent("Done"));
        Func<Task> bypass = async () => await legacy.RestartAsync((IFSMNode)node);
        await bypass.Should().ThrowAsync<FSMException>().WithMessage("*Typed execution*");
    }

    [Fact]
    public async Task LegacyStartCannotBypassPreparedEngineOwnership()
    {
        var engine = Engine();
        using var prepared = engine.BindContext<Input>("Start", "Done").Prepare(new Input());
        using var legacy = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        Func<Task> start = async () => await legacy.RestartAsync();
        await start.Should().ThrowAsync<FSMException>();
        using var execution = await prepared.StartAsync();
        await execution.StopAsync();
    }

    private sealed class EqualIdle : BaseFSMNode
    {
        public override bool Equals(object? obj) => obj is EqualIdle;
        public override int GetHashCode() => 1;
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }

    [Fact]
    public void StandaloneLegacyGraphsCheckSharedNodesByReferenceNotBusinessEquality()
    {
        IFSMNode first = new EqualIdle { Name = "First" };
        IFSMNode second = new EqualIdle { Name = "Second" };
        IFSMNode shared = new EqualIdle { Name = "Shared" };
        var go = new FSMEvent("Go");
        first.AddTransition(go, shared);
        second.AddTransition(go, shared);
        var done = new FSMEvent("Done");
        using var owner = new FSMExecutor(first, done);
        Action conflict = () => { using var invalid = new FSMExecutor(second, done); };
        conflict.Should().Throw<FSMException>().WithMessage("*Shared*owner*");
    }

    [Fact]
    public void NodeInstanceCannotBeAssembledIntoTwoEngines()
    {
        var node = new Idle();
        var factory = new Factory(() => node);
        var first = new FSMEngine(factory);
        first.CreateNode("Idle", "Original");
        var second = new FSMEngine(factory);
        Action assemble = () => second.CreateNode("OtherType", "Conflicting");
        assemble.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Original").And.Contain("Conflicting").And.Contain("owner");
        node.Name.Should().Be("Original");
        ((IFSMNode)node).ClassType.Should().Be("Idle");
        first.GetNode("Original").Should().BeSameAs(node);
    }

    [Fact]
    public async Task PreparedExecutionOwnsEngineUntilExplicitlyReleased()
    {
        var flow = Engine().BindContext<Input>("Start", "Done");
        var prepared = flow.Prepare(new Input());
        Action duplicate = () => flow.Prepare(new Input());
        duplicate.Should().Throw<FSMException>().WithMessage("*execution*");
        prepared.Dispose();
        using var next = flow.Prepare(new Input());
        Func<Task> staleStart = async () => await prepared.StartAsync();
        await staleStart.Should().ThrowAsync<FSMException>();
    }
}
