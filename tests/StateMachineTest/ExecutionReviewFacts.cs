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
    public async Task EveryRestartRejectsForeignDescendantsAddedAfterConstructionBeforeRebinding(int overload)
    {
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        var foreign = new Probe(async () => { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; });
        var b = Engine(("Borrowed", foreign));
        using var activeB = new FSMExecutor(foreign, b.GetEvent("Done"));
        await activeB.RestartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var originalB = ((IFSMNode)foreign).Context;
        var a = Engine(("Start", new Probe()));
        using var executor = new FSMExecutor(a.GetNode("Start"), a.GetEvent("Done"));
        var originalA = a.GetNode("Start").Context;
        IFSMNode replacement = overload < 2 ? a.GetNode("Start") : new Probe { Name = "Replacement" };
        replacement.AddTransition(new FSMEvent("Go"), foreign);
        try
        {
            Func<Task> restart = () => overload switch
            {
                0 => executor.RestartAsync(),
                1 => executor.RestartAsync(new object()),
                2 => executor.RestartAsync(replacement),
                _ => executor.RestartAsync(replacement, new object())
            };
            await restart.Should().ThrowAsync<FSMException>().WithMessage("*Borrowed*owner*engine*requested engine*");
            ((IFSMNode)foreign).Context.Should().BeSameAs(originalB);
            a.GetNode("Start").Context.Should().BeSameAs(originalA);
            calls.Should().Be(1);
            activeB.Execution.Completion.IsCompleted.Should().BeFalse();
        }
        finally { release.TrySetResult(); await Task.WhenAll(activeB.StopAsync(), executor.StopAsync()); }
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
    public async Task EveryLegacyRestartValidatesRequirementsAddedAfterConstruction(int overload)
    {
        var visits = 0;
        var engine = Engine(("Start", new Probe(() => { visits++; return Task.CompletedTask; })));
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        IFSMNode root = overload < 2 ? engine.GetNode("Start") : new Probe { Name = "Replacement" };
        IFSMNode required = new RequiresBusiness { Name = "Required" };
        root.AddTransition(new FSMEvent("Go"), required);
        var original = required.Context;
        Func<Task> restart = () => overload switch
        {
            0 => executor.RestartAsync(),
            1 => executor.RestartAsync(new object()),
            2 => executor.RestartAsync(root),
            _ => executor.RestartAsync(root, new object())
        };
        await restart.Should().ThrowAsync<FSMException>().WithMessage("*Typed execution*");
        visits.Should().Be(0);
        required.Context.Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ResumeAcceptedAtNodeExitCannotLeakIntoAnyRestartOverload(int overload)
    {
        var runs = 0;
        var targetEntered = Signal();
        var start = new Probe(() => { Interlocked.Increment(ref runs); return Task.CompletedTask; });
        var engine = Engine(("Start", start), ("Target", new Probe(() => { targetEntered.TrySetResult(); return Task.CompletedTask; })));
        engine.ConnectNode("Go", "Start", "Target");
        using var executor = new FSMExecutor(start, engine.GetEvent("Done"));
        var firstExit = true;
        executor.NodeExitChanged += (_, _) =>
        {
            if (!firstExit) return;
            firstExit = false;
            executor.Pause();
            executor.Continue().Should().BeTrue();
            executor.Stop();
        };
        await executor.RestartAsync();
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await (overload switch
        {
            0 => executor.RestartAsync(),
            1 => executor.RestartAsync(new object()),
            2 => executor.RestartAsync((IFSMNode)start),
            _ => executor.RestartAsync((IFSMNode)start, new object())
        });
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

    private sealed class Initializing : BaseFSMNode
    {
        public TaskCompletionSource Entered { get; } = Signal();
        public ManualResetEventSlim Release { get; } = new();
        public override void InitBeforeStart() { Entered.TrySetResult(); Release.Wait(); }
        protected override Task ExecuteAsync() => Task.CompletedTask;
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
    public async Task UnscopedHandleCallbacksCannotDeliverIntoALaterExecution(string eventName)
    {
        var engine = Engine(("Start", new Probe()), ("Target", new Probe()));
        engine.ConnectNode("Go", "Start", "Target");
        using var executor = new FSMExecutor(engine.GetNode("Start"), engine.GetEvent("Done"));
        await executor.RestartAsync();
        await State(executor, FSMState.Running);
        Action<FSMEvent> oldReceiver = ((IHandle<FSMEvent>)executor).Handle;
        await executor.Execution.StopAsync();
        await executor.RestartAsync();
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
