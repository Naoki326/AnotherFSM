using StateMachine;

namespace StateMachineTest;

public class ExecutionReviewFacts
{
    public sealed class Input { }
    private sealed class Probe(Func<Task>? run = null) : BaseFSMNode
    {
        protected override Task ExecuteAsync() => run?.Invoke() ?? Task.CompletedTask;
    }
    private sealed class Factory(Dictionary<string, IFSMNode> nodes) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => nodes[key];
        public Type GetNodeType(string key) => nodes[key].GetType();
        public string GetNodeFeatureName(Type type) => nodes.First(n => n.Value.GetType() == type).Key;
    }
    private static FSMEngine Engine(params (string Name, IFSMNode Node)[] nodes)
    {
        var engine = new FSMEngine(new Factory(nodes.ToDictionary(n => n.Name, n => n.Node)));
        foreach (var node in nodes) engine.CreateNode(node.Name, node.Name);
        engine.AddEvent(new FSMEvent("Done"));
        return engine;
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<FSMExecutor> Start(FSMEngine engine, int shape, Action<FSMExecutor>? observe = null)
    {
        using var prepared = engine.Prepare();
        return shape % 2 == 0
            ? prepared.StartAsync("Start", new[] { "Done" }, observe, shape >= 2)
            : prepared.StartAsync("Start", new[] { engine.GetEvent("Done") }, observe, shape >= 2);
    }
    private static async Task State(FSMExecutor executor, FSMState expected)
    {
        var reached = Signal();
        void Changed(FSMExecutor _, FSMState now, FSMState prior) { if (now == expected) reached.TrySetResult(); }
        executor.FSMStateChanged += Changed;
        try
        {
            if (executor.State == expected) reached.TrySetResult();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { executor.FSMStateChanged -= Changed; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EveryPreparationStartShapeRejectsForeignDescendantsBeforeRebinding(int overload)
    {
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        var foreign = new Probe(async () => { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; });
        var b = Engine(("Borrowed", foreign));
        using var activeB = await b.Prepare().StartAsync("Borrowed", "Done");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var originalB = foreign.Control.Token;
        var a = Engine(("Start", new Probe()), ("Replacement", new Probe()));
        var originalA = a.GetNode("Start").Control.Token;
        IFSMNode replacement = overload < 2 ? a.GetNode("Start") : a.GetNode("Replacement");
        replacement.AddTransition(new FSMEvent("Go"), foreign);
        try
        {
            Func<Task> restart = () => Start(a, overload);
            await restart.Should().ThrowAsync<FSMException>().WithMessage("*Borrowed*owner*requested engine*");
            foreign.Control.Token.Should().Be(originalB);
            a.GetNode("Start").Control.Token.Should().Be(originalA);
            calls.Should().Be(1);
            activeB.Execution.Completion.IsCompleted.Should().BeFalse();
        }
        finally { release.TrySetResult(); await activeB.StopAsync(); }
    }

    private sealed class RequiredBusiness { }
    private sealed class RequiresBusiness : BaseFSMNode<RequiredBusiness>
    {
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EveryPreparationStartShapeRejectsUnassembledDependentDescendants(int overload)
    {
        var visits = 0;
        var engine = Engine(("Start", new Probe(() => { visits++; return Task.CompletedTask; })));
        IFSMNode root = engine.GetNode("Start");
        IFSMNode required = new RequiresBusiness { Name = "Required" };
        root.AddTransition(new FSMEvent("Go"), required);
        var original = required.Control.Token;
        Func<Task> restart = () => Start(engine, overload);
        await restart.Should().ThrowAsync<FSMException>().WithMessage("*Required*owner*");
        visits.Should().Be(0);
        required.Control.Token.Should().Be(original);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ResumeAcceptedAtNodeExitCannotLeakIntoAnyPreparationStartShape(int overload)
    {
        var runs = 0;
        var targetEntered = Signal();
        var start = new Probe(() => { Interlocked.Increment(ref runs); return Task.CompletedTask; });
        var engine = Engine(("Start", start), ("Target", new Probe(() => { targetEntered.TrySetResult(); return Task.CompletedTask; })));
        engine.ConnectNode("Go", "Start", "Target");
        var firstExit = true;
        using var first = await Start(engine, overload, value => value.NodeExitChanged += (_, _) =>
        {
            if (!firstExit) return;
            firstExit = false;
            value.Pause();
            value.Continue().Should().BeTrue();
            value.Stop();
        });
        await first.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        using var executor = await Start(engine, overload);
        await State(executor, FSMState.Running);
        try
        {
            executor.Execution.PublishEvent("Go");
            await targetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            runs.Should().Be(2);
        }
        finally { await executor.StopAsync(); }
    }

    // Control the public async scheduling boundary, not executor internals. A pump
    // returning proves the consumer either parked at the pause gate or advanced.
    private sealed class PumpContext : SynchronizationContext
    {
        private readonly System.Threading.Channels.Channel<(SendOrPostCallback Callback, object? Value)> work
            = System.Threading.Channels.Channel.CreateUnbounded<(SendOrPostCallback, object?)>();
        public override void Post(SendOrPostCallback callback, object? value) => work.Writer.TryWrite((callback, value));
        public async Task Next()
        {
            var item = await work.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var previous = Current;
            SetSynchronizationContext(this);
            try { item.Callback(item.Value); }
            finally { SetSynchronizationContext(previous); }
        }
        public async Task DrainUntil(Task task)
        {
            while (!task.IsCompleted)
            {
                var ready = work.Reader.WaitToReadAsync().AsTask();
                if (await Task.WhenAny(ready, task).WaitAsync(TimeSpan.FromSeconds(5)) == task) break;
                await Next();
            }
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
    private sealed class Interceptor(Action action) : IFSMEventInterceptor
    {
        public FSMEvent EventEntry(FSMEvent value) { action(); return value; }
    }

    [Fact]
    public async Task PauseDuringInterceptionPreservesTheTransitionUntilContinue()
    {
        var targetRuns = 0;
        var target = new Probe(() => { targetRuns++; return Task.CompletedTask; });
        var engine = Engine(("Start", new Probe()), ("Target", target));
        engine.ConnectNode("Go", "Start", "Target");
        var context = new PumpContext();
        FSMExecutor executor;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try { executor = await engine.BindContext<Input>("Start", "Done").Prepare(new Input()).StartAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        using (executor)
        using (var release = new ManualResetEventSlim())
        {
            await context.Next(); // Initial node and the consumer's channel wait.
            var intercepted = Signal();
            var selected = new List<string>();
            executor.NodeStateChanged += (_, name) => selected.Add(name);
            executor.EventInterceptor = new Interceptor(() => { intercepted.TrySetResult(); release.Wait(); });
            executor.Execution.PublishEvent("Go", "original payload");
            await context.Next(); // Channel wake-up and the cooperative timer.
            var processing = Task.Run(context.Next);
            await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                executor.Execution.Pause();
                executor.State.Should().Be(FSMState.Paused);
                release.Set();
                await processing.WaitAsync(TimeSpan.FromSeconds(5));
                selected.Should().BeEmpty();
                targetRuns.Should().Be(0);
                executor.Execution.Continue().Should().BeTrue();
                await context.Next();
                selected.Should().Equal("Target");
                targetRuns.Should().Be(1);
                target.TriggerEvent!.EventName.Should().Be("Go");
                target.TriggerEvent.EventContext.Should().Be("original payload");
            }
            finally
            {
                release.Set();
                await processing;
                await context.DrainUntil(executor.StopAsync());
            }
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task StopDuringEveryPreparationStartShapeInitializationWaitsAndPreventsNodeExecution(int overload, bool dispose)
    {
        var node = new Initializing();
        var engine = Engine(("Start", node));
        FSMExecutor executor = null!;
        var starting = Task.Run(() => Start(engine, overload, value => executor = value));
        await node.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task? stopping = null;
        try
        {
            if (dispose) executor.Dispose();
            stopping = executor.StopAsync();
            stopping.IsCompleted.Should().BeFalse("initialization has not returned");
            executor.ExecutorTask.Should().NotBeNull();
            executor.Execution.Completion.IsCompleted.Should().BeFalse();
            Action prepare = () => engine.BindContext<Input>("Start", "Done").Prepare(new Input());
            prepare.Should().Throw<FSMException>();
        }
        finally
        {
            node.Release.Set();
            await starting.WaitAsync(TimeSpan.FromSeconds(5));
            await (stopping ?? executor.StopAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        }
        node.Runs.Should().Be(0);
        executor.State.Should().Be(FSMState.Stoped);
        using var next = engine.BindContext<Input>("Start", "Done").Prepare(new Input());
    }

    [Theory]
    [InlineData(-1)] // Typed Start.
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InitializationFailureNeverRunsNodesAndReleasesOnlyTheFailedRound(int overload)
    {
        var node = new Initializing { Fail = true };
        var engine = Engine(("Start", node));
        var flow = engine.BindContext<Input>("Start", "Done");
        FSMExecutor executor = null!;
        var starting = Task.Run(async () =>
        {
            if (overload == -1) await flow.Prepare(new Input()).StartAsync("Start", new[] { "Done" }, value => executor = value);
            else await Start(engine, overload, value => executor = value);
        });
        await node.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            starting.IsCompleted.Should().BeFalse();
            Func<Task> prepare = () => Task.Run(() => flow.Prepare(new Input())).WaitAsync(TimeSpan.FromSeconds(5));
            await prepare.Should().ThrowAsync<FSMException>();
            executor.Execution.Completion.IsCompleted.Should().BeFalse();
        }
        finally { node.Release.Set(); }
        Func<Task> failure = () => starting.WaitAsync(TimeSpan.FromSeconds(5));
        await failure.Should().ThrowAsync<InvalidOperationException>().WithMessage("initialization failure");
        Func<Task> completion = () => executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await completion.Should().ThrowAsync<InvalidOperationException>();
        executor.Dispose();
        node.Runs.Should().Be(0);
        node.Fail = false;
        using var next = await flow.Prepare(new Input()).StartAsync();
        await next.StopAsync();
    }

    private sealed class InlineCancellation(bool fail) : BaseFSMNode
    {
        private readonly TaskCompletionSource release = new(); // Deliberately allow inline node exit.
        public TaskCompletionSource Held { get; } = Signal();
        public ManualResetEventSlim Return { get; } = new();
        private int runs;
        protected override async Task ExecuteAsync()
        {
            if (Interlocked.Increment(ref runs) != 1) return;
            using var registration = Control.Token.Register(() =>
            {
                release.SetResult();
                Held.TrySetResult();
                Return.Wait();
                if (fail) throw new InvalidOperationException("inline cancellation failure");
            });
            await release.Task.ConfigureAwait(false);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationCallbackMustReturnAndReportErrorsBeforeCompletionOrEngineReuse(bool fail)
    {
        var node = new InlineCancellation(fail);
        var engine = Engine(("Start", node));
        var flow = engine.BindContext<Input>("Start", "Done");
        using var first = await Task.Run(() => flow.Prepare(new Input()).StartAsync());
        await State(first, FSMState.Running);
        var original = node.Control.Token;
        var stopping = Task.Run(first.Execution.Stop);
        await node.Held.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            first.Execution.Completion.IsCompleted.Should().BeFalse("the cancellation callback still owns this round");
            (await Task.Run(first.Execution.Continue).WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
            Func<Task> competing = () => Task.Run(() => flow.Prepare(new Input())).WaitAsync(TimeSpan.FromSeconds(5));
            await competing.Should().ThrowAsync<FSMException>();
            node.Control.Token.Should().Be(original);
        }
        finally { node.Return.Set(); await stopping.WaitAsync(TimeSpan.FromSeconds(5)); }
        if (fail)
        {
            Func<Task> completion = () => first.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await completion.Should().ThrowAsync<AggregateException>();
            first.StopException.Should().BeOfType<AggregateException>();
        }
        else await first.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        using var next = await flow.Prepare(new Input()).StartAsync();
        await next.StopAsync();
    }

    private sealed class CooperativeInitialization
    {
        public TaskCompletionSource Entered { get; } = Signal();
        public TaskCompletionSource Cancelled { get; } = Signal();
        public ManualResetEventSlim CancellationOrRelease { get; } = new();
        public ManualResetEventSlim Return { get; } = new();
        public int Runs;
        public Action? OnCancel;
        public void Initialize(FSMExecutionControl control)
        {
            using var registration = control.Token.Register(() =>
            {
                Cancelled.TrySetResult();
                CancellationOrRelease.Set();
                OnCancel?.Invoke();
            });
            Entered.TrySetResult();
            CancellationOrRelease.Wait();
            Return.Wait();
        }
        public void Release() { CancellationOrRelease.Set(); Return.Set(); }
    }
    private sealed class CooperativeInitializer(CooperativeInitialization probe) : BaseFSMNode
    {
        public override void InitBeforeStart() => probe.Initialize(Control);
        protected override Task ExecuteAsync() { Interlocked.Increment(ref probe.Runs); return Task.CompletedTask; }
    }
    private sealed class InitializationInput { public Input Business { get; } = new(); }
    private sealed class TypedCooperativeInitializer(CooperativeInitialization probe) : BaseFSMNode<Input>
    {
        public override void InitBeforeStart() => probe.Initialize(Control);
        protected override Task ExecuteAsync() { Interlocked.Increment(ref probe.Runs); return Task.CompletedTask; }
    }
    private sealed class InitializationCounter(Action initialize, Action run) : BaseFSMNode
    {
        public override void InitBeforeStart() => initialize();
        protected override Task ExecuteAsync() { run(); return Task.CompletedTask; }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task StopCancelsTheNonStartInitializerAndDoesNotReleaseLaterNodes(bool typed, bool unreachable, bool dispose)
    {
        var probe = new CooperativeInitialization();
        var runs = 0;
        var laterInitializations = 0;
        IFSMNode initializer = typed ? new TypedCooperativeInitializer(probe) : new CooperativeInitializer(probe);
        var engine = Engine(("Start", new Probe(() => { runs++; return Task.CompletedTask; })),
            ("Next", initializer), ("Later", new InitializationCounter(() => laterInitializations++, () => runs++)));
        if (!unreachable) engine.ConnectNode("Go", "Start", "Next");
        using var preparation = typed ? engine.BindContext<InitializationInput>("Start", "Done").Prepare(new InitializationInput()) : engine.Prepare();
        FSMExecutor executor = null!;
        var starting = Task.Run(() => preparation.StartAsync("Start", new[] { "Done" }, value => executor = value));
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        probe.OnCancel = () => Task.Run(executor.Execution.Continue).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult().Should().BeFalse();
        try
        {
            if (dispose) executor.Dispose();
            else executor.Execution.Stop();
            initializer.Control.Token.IsCancellationRequested.Should().BeTrue("Stop must reach the node currently initializing, even outside the start node's reachable graph");
            probe.Cancelled.Task.IsCompleted.Should().BeTrue();
            executor.Execution.Completion.IsCompleted.Should().BeFalse("the initializer has not returned");
            engine.HasUnexitedExecution.Should().BeTrue();
            executor.Execution.Continue().Should().BeFalse();
            Action conflict = () => engine.Prepare();
            conflict.Should().Throw<FSMException>();
        }
        finally { probe.Release(); }
        using var finished = await starting.WaitAsync(TimeSpan.FromSeconds(5));
        await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        probe.Runs.Should().Be(0);
        runs.Should().Be(0);
        laterInitializations.Should().Be(0, "stopping must not admit another initializer");
        executor.StopException.Should().BeNull("cancellation callbacks can use public controls without waiting on the control lock");
        engine.HasUnexitedExecution.Should().BeFalse();
        using var next = typed ? engine.BindContext<InitializationInput>("Start", "Done").Prepare(new InitializationInput()) : engine.Prepare();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SharedStartCancelsItsActiveInitializerAndStopBeforeAdmissionSkipsTheOtherInitializers(bool typed, bool dispose)
    {
        var probe = new CooperativeInitialization();
        var runs = 0;
        var laterInitializations = 0;
        IFSMNode initializer = typed ? new TypedCooperativeInitializer(probe) : new CooperativeInitializer(probe);
        var a = Engine(("Start", new Probe(() => { runs++; return Task.CompletedTask; })), ("Next", initializer));
        var b = Engine(("Start", new InitializationCounter(() => laterInitializations++, () => runs++)));
        using var pa = (typed ? a.BindContext<InitializationInput>("Start", "Done").Prepare(new InitializationInput()) : a.Prepare()).WithEntry("Start", "Done");
        using var pb = b.Prepare().WithEntry("Start", "Done");
        FSMEventSharing.Connect(pa, pb);
        FSMExecutor first = null!, second = null!;
        pa.WithEntry("Start", new[] { "Done" }, value => first = value);
        pb.WithEntry("Start", new[] { "Done" }, value => second = value);
        var starting = Task.Run(() => FSMEventSharing.StartAsync(pa, pb));
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            second.Execution.Completion.IsCompleted.Should().BeFalse("every receiver and round is ready before initialization is released");
            if (dispose) first.Dispose();
            else first.Execution.Stop();
            probe.Cancelled.Task.IsCompleted.Should().BeTrue();
            second.Execution.State.Should().Be(FSMState.Initialized, "control is not shared");
            b.GetNode("Start").Control.Token.IsCancellationRequested.Should().BeFalse();
            second.Execution.Stop(); // Stop before this engine's initializer is admitted.
            first.Execution.Completion.IsCompleted.Should().BeFalse();
            second.Execution.Completion.IsCompleted.Should().BeFalse();
            a.HasUnexitedExecution.Should().BeTrue();
            b.HasUnexitedExecution.Should().BeTrue();
        }
        finally { probe.Release(); }
        var executors = await starting.WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var executor in executors)
        {
            await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            executor.Dispose();
        }
        probe.Runs.Should().Be(0);
        runs.Should().Be(0);
        laterInitializations.Should().Be(0);
        a.HasUnexitedExecution.Should().BeFalse();
        b.HasUnexitedExecution.Should().BeFalse();
    }

    private sealed class Initializing : BaseFSMNode
    {
        public TaskCompletionSource Entered { get; } = Signal();
        public ManualResetEventSlim Release { get; } = new();
        public int Runs;
        public bool Fail;
        public override void InitBeforeStart()
        {
            Entered.TrySetResult();
            Release.Wait();
            if (Fail) throw new InvalidOperationException("initialization failure");
        }
        protected override Task ExecuteAsync() { Interlocked.Increment(ref Runs); return Task.CompletedTask; }
    }

    [Fact]
    public async Task DuplicatePreparationStartRejectsWithoutWaitingForUserInitialization()
    {
        var node = new Initializing();
        var engine = Engine(("Start", node));
        using var preparation = engine.BindContext<Input>("Start", "Done").Prepare(new Input());
        var first = Task.Run(() => preparation.StartAsync());
        await node.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task? competitor = null;
        try
        {
            competitor = Task.Run(() => preparation.StartAsync());
            Func<Task> duplicate = () => competitor.WaitAsync(TimeSpan.FromSeconds(2));
            await duplicate.Should().ThrowAsync<FSMException>().WithMessage("*already started*");
        }
        finally { node.Release.Set(); }
        using var executor = await first.WaitAsync(TimeSpan.FromSeconds(5));
        try { if (competitor != null) await competitor; } catch (FSMException) { }
        await executor.StopAsync();
    }

    [Theory]
    [InlineData("Go")]
    [InlineData("Done")]
    public async Task SavedReceiverCallbacksCannotDeliverIntoALaterExecution(string eventName)
    {
        var engine = Engine(("Start", new Probe()), ("Target", new Probe()));
        engine.ConnectNode("Go", "Start", "Target");
        using var first = await engine.Prepare().StartAsync("Start", "Done");
        await State(first, FSMState.Running);
        var old = first.Execution;
        Action<FSMEvent> oldReceiver = value => old.PublishEvent(value.EventName, value.EventContext);
        await old.StopAsync();
        using var executor = await engine.Prepare().StartAsync("Start", "Done");
        await State(executor, FSMState.Running);
        try
        {
            Action stale = () => oldReceiver((FSMEvent)engine.GetEvent(eventName).Clone());
            stale.Should().Throw<FSMException>().WithMessage("*Execution*");
            executor.State.Should().Be(FSMState.Running);
            executor.Execution.Completion.IsCompleted.Should().BeFalse();
        }
        finally { await executor.StopAsync(); }
    }
}
