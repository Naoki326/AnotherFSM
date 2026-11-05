using System.Threading.Channels;
using StateMachine;

namespace StateMachineTest;

public class EventSharingFacts
{
    public sealed class Probe
    {
        public Channel<string> Visits { get; } = Channel.CreateUnbounded<string>();
        public bool PublishInitially { get; init; }
        public int Starts;
    }
    public sealed class Input { public Probe Probe { get; init; } = new(); }
    private sealed class Node : BaseFSMNode<Probe>
    {
        protected override Task ExecuteAsync()
        {
            Context.Visits.Writer.TryWrite(TriggerEvent?.EventName ?? "start");
            if (TriggerEvent == null && Interlocked.Increment(ref Context.Starts) == 1 && Context.PublishInitially)
                PublishEvent(1);
            return Task.CompletedTask;
        }
    }
    private sealed class Factory : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new Node();
        public Type GetNodeType(string key) => typeof(Node);
        public string GetNodeFeatureName(Type type) => "Probe";
    }
    private static FSMEngine Engine(FSMEvent? ping = null)
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateNode("Probe", "Start");
        engine.AddEvent(ping ?? new FSMEvent("Ping"));
        foreach (var name in new[] { "Fence", "Done" }) engine.AddEvent(new FSMEvent(name));
        engine.AttachEvent("Start", "Ping", 1);
        engine.ConnectNode("Ping", "Start", "Start");
        engine.ConnectNode("Fence", "Start", "Start");
        return engine;
    }
    private static async Task<string> Next(Probe probe)
        => await probe.Visits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task OverlapIsIdempotentBidirectionalAndSurvivesAnIntermediateExitWithoutSharingContextOrControl()
    {
        var inputs = Enumerable.Range(0, 4).Select(_ => new Input()).ToArray();
        var preparations = inputs.Select(i => Engine().BindContext<Input>("Start", "Done").Prepare(i)).ToArray();
        FSMEventSharing.Connect(preparations[0], preparations[1], preparations[2]);
        FSMEventSharing.Connect(preparations[2], preparations[3]);
        FSMEventSharing.Connect(preparations[0], preparations[1], preparations[0]);
        var executors = await FSMEventSharing.StartAsync(preparations);
        try
        {
            foreach (var input in inputs) (await Next(input.Probe)).Should().Be("start");
            await executors[1].StopAsync();
            foreach (var sender in new[] { 0, 3, 0 })
            {
                executors[sender].Execution.PublishEvent("Ping");
                executors[sender].Execution.PublishEvent("Fence");
                foreach (var index in new[] { 0, 2, 3 })
                {
                    (await Next(inputs[index].Probe)).Should().Be("Ping");
                    (await Next(inputs[index].Probe)).Should().Be("Fence");
                }
            }
            inputs.Should().OnlyContain(i => i.Probe.Starts == 1);
            executors[0].Execution.Pause();
            executors[0].State.Should().Be(FSMState.Paused);
            executors[2].State.Should().Be(FSMState.Running);
            executors[3].State.Should().Be(FSMState.Running);
        }
        finally
        {
            foreach (var executor in executors) await executor.StopAsync();
            foreach (var preparation in preparations) preparation.Dispose();
        }
    }

    [Fact]
    public async Task FailedConnectCannotMergeRangesAndClonedEventIdsDoNotGrantMembership()
    {
        var ping = new FSMEvent("Ping");
        var inputs = Enumerable.Range(0, 4).Select(_ => new Input()).ToArray();
        var preparations = inputs.Select(i => Engine((FSMEvent)ping.Clone()).BindContext<Input>("Start", "Done").Prepare(i)).ToArray();
        FSMEventSharing.Connect(preparations[0], preparations[1]);
        FSMEventSharing.Connect(preparations[2], preparations[3]);
        using var invalid = Engine().BindContext<Input>("Start", "Done").Prepare(new Input());
        invalid.Dispose();
        Action bad = () => FSMEventSharing.Connect(preparations[0], preparations[2], invalid);
        bad.Should().Throw<FSMException>();
        Action nullMember = () => FSMEventSharing.Connect(preparations[0], preparations[2], null!);
        nullMember.Should().Throw<FSMException>();
        var executors = await FSMEventSharing.StartAsync(preparations);
        try
        {
            foreach (var input in inputs) (await Next(input.Probe)).Should().Be("start");
            executors[0].Execution.PublishEvent("Ping");
            executors[0].Execution.PublishEvent("Fence");
            executors[2].Execution.PublishEvent("Fence");
            foreach (var index in new[] { 0, 1 })
            {
                (await Next(inputs[index].Probe)).Should().Be("Ping");
                (await Next(inputs[index].Probe)).Should().Be("Fence");
            }
            foreach (var index in new[] { 2, 3 }) (await Next(inputs[index].Probe)).Should().Be("Fence");
            FSMEventSharing.Connect(executors[1].Execution, executors[2].Execution);
            executors[3].Execution.PublishEvent("Ping");
            foreach (var input in inputs) (await Next(input.Probe)).Should().Be("Ping");
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task AGraphInvalidatedDuringReceiverPreparationCannotReleaseAnyNode()
    {
        var engine = Engine();
        var a = new Input();
        var b = new Input();
        using var pa = engine.BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = Engine().BindContext<Input>("Start", "Done").Prepare(b);
        pb.ReceiverPreparation = () => { engine.CreateNode("Probe", "Added"); return new ReceiverLease(); };
        FSMEventSharing.Connect(pa, pb);
        IReadOnlyList<FSMExecutor>? released = null;
        try
        {
            Func<Task> start = async () => released = await FSMEventSharing.StartAsync(pa, pb);
            await start.Should().ThrowAsync<FSMException>();
            a.Probe.Starts.Should().Be(0);
            b.Probe.Starts.Should().Be(0);
        }
        finally { if (released != null) foreach (var executor in released) await executor.StopAsync(); }
    }

    [Fact]
    public async Task ReceiverCleanupErrorsAreInsideTheTrueExitBarrierAndCannotLeakEngineOwnership()
    {
        var engine = Engine();
        var flow = engine.BindContext<Input>("Start", "Done");
        using var preparation = flow.Prepare(new Input());
        var lease = new ReceiverLease(true);
        preparation.ReceiverPreparation = () => lease;
        using var executor = await preparation.StartAsync();
        Func<Task> stop = async () => await executor.StopAsync();
        await stop.Should().ThrowAsync<InvalidOperationException>().WithMessage("receiver rollback failure");
        lease.Disposed.Should().BeTrue();
        executor.Execution.Completion.IsFaulted.Should().BeTrue();
        using var next = flow.Prepare(new Input());
    }

    [Fact]
    public async Task RunningRangeKeepsWorkingDuringFailedJoinAndNeverReplaysPreJoinNotifications()
    {
        var inputs = Enumerable.Range(0, 4).Select(_ => new Input()).ToArray();
        var preparations = inputs.Select(i => Engine().BindContext<Input>("Start", "Done").Prepare(i)).ToArray();
        FSMEventSharing.Connect(preparations[0], preparations[1]);
        var old = await FSMEventSharing.StartAsync(preparations[0], preparations[1]);
        IReadOnlyList<FSMExecutor>? added = null;
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLease = new ReceiverLease();
        preparations[2].ReceiverPreparation = () => firstLease;
        preparations[3].ReceiverPreparation = () =>
        {
            preparing.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("controlled receiver preparation failure");
        };
        try
        {
            foreach (var index in new[] { 0, 1 }) (await Next(inputs[index].Probe)).Should().Be("start");
            FSMEventSharing.Connect(old[0].Execution, preparations[2], preparations[3]);
            Func<Task> incomplete = async () => await preparations[2].StartAsync();
            await incomplete.Should().ThrowAsync<FSMException>().WithMessage("*pending*");
            var attempt = Task.Run(() => FSMEventSharing.StartAsync(preparations[2], preparations[3]));
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                inputs[2].Probe.Starts.Should().Be(0);
                inputs[3].Probe.Starts.Should().Be(0);
                Action connectDuringStart = () => FSMEventSharing.Connect(old[1].Execution, preparations[2]);
                connectDuringStart.Should().Throw<FSMException>();
                Func<Task> duplicate = async () => await preparations[2].StartAsync();
                await duplicate.Should().ThrowAsync<FSMException>();
                Action disposeDuringStart = () => preparations[3].Dispose();
                disposeDuringStart.Should().Throw<FSMException>();
                old[0].Execution.PublishEvent("Ping");
                old[0].Execution.PublishEvent("Fence");
                foreach (var index in new[] { 0, 1 })
                {
                    (await Next(inputs[index].Probe)).Should().Be("Ping");
                    (await Next(inputs[index].Probe)).Should().Be("Fence");
                }
            }
            finally { release.TrySetResult(); }
            Func<Task> failed = async () => await attempt.WaitAsync(TimeSpan.FromSeconds(5));
            (await failed.Should().ThrowAsync<FSMException>()).WithInnerException<InvalidOperationException>();
            firstLease.Disposed.Should().BeTrue();
            inputs[2].Probe.Starts.Should().Be(0);
            inputs[3].Probe.Starts.Should().Be(0);
            old[1].Execution.PublishEvent("Ping");
            foreach (var index in new[] { 0, 1 }) (await Next(inputs[index].Probe)).Should().Be("Ping");
            preparations[2].ReceiverPreparation = preparations[3].ReceiverPreparation = null;
            added = await FSMEventSharing.StartAsync(preparations[2], preparations[3]);
            foreach (var index in new[] { 2, 3 }) (await Next(inputs[index].Probe)).Should().Be("start");
            old[0].Execution.PublishEvent("Fence");
            foreach (var input in inputs) (await Next(input.Probe)).Should().Be("Fence");
            inputs.Should().OnlyContain(i => i.Probe.Starts == 1);
            Func<Task> restartOld = async () => await FSMEventSharing.StartAsync(preparations[0]);
            await restartOld.Should().ThrowAsync<FSMException>();
        }
        finally
        {
            release.TrySetResult();
            if (added != null) foreach (var executor in added) await executor.StopAsync();
            foreach (var executor in old) await executor.StopAsync();
            foreach (var preparation in preparations) preparation.Dispose();
        }
    }

    [Fact]
    public async Task StartRejectsInvalidOrIncompleteSelectionsWithoutConsumingValidPreparations()
    {
        var ea = Engine();
        var a = new Input();
        var b = new Input();
        using var pa = ea.BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = Engine().BindContext<Input>("Start", "Done").Prepare(b);
        using var invalid = Engine().BindContext<Input>("Start", "Done").Prepare(new Input());
        invalid.Dispose();
        FSMEventSharing.Connect(pa, pb);
        foreach (var selection in new[] { Array.Empty<FSMContextPreparation>(), new[] { pa }, new[] { pa, pa, pb }, new[] { pa, pb, invalid }, new[] { pa, pb, null! } })
        {
            Func<Task> start = async () => await FSMEventSharing.StartAsync(selection);
            await start.Should().ThrowAsync<FSMException>();
            a.Probe.Starts.Should().Be(0);
            b.Probe.Starts.Should().Be(0);
        }
        Func<Task> singleBypass = async () => await pa.StartAsync("Start", "Done");
        await singleBypass.Should().ThrowAsync<FSMException>().WithMessage("*pending*");
        Action occupied = () => ea.BindContext<Input>("Start", "Done").Prepare(new Input());
        occupied.Should().Throw<FSMException>();
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        try { (await Next(a.Probe)).Should().Be("start"); (await Next(b.Probe)).Should().Be("start"); }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
        using var retry = ea.BindContext<Input>("Start", "Done").Prepare(new Input());
    }

    private sealed class BareNode(Probe probe) : BaseFSMNode
    {
        protected override Task ExecuteAsync()
        {
            probe.Visits.Writer.TryWrite(TriggerEvent?.EventName ?? "start");
            return Task.CompletedTask;
        }
    }
    private sealed class BareFactory(Probe probe) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new BareNode(probe);
        public Type GetNodeType(string key) => typeof(BareNode);
        public string GetNodeFeatureName(Type type) => "Bare";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignClonedIdsCannotBypassTargetLocalNameRegistrationForOrdinaryOrEndEvents(bool end)
    {
        var source = Engine();
        var input = new Input();
        using var pa = source.BindContext<Input>("Start", "Done").Prepare(input);
        using var a = await pa.StartAsync();
        (await Next(input.Probe)).Should().Be("start");
        var probe = new Probe();
        var target = new FSMEngine(new BareFactory(probe));
        target.CreateNode("Bare", "Start");
        target.AddEvent(new FSMEvent("Fence"));
        target.AddEvent(new FSMEvent("Done"));
        var foreign = (FSMEvent)source.GetEvent("Ping").Clone();
        target.GetNode("Start").AddTransition(foreign, target.GetNode("Start"));
        target.ConnectNode("Fence", "Start", "Start");
        using var b = new FSMExecutor(target.GetNode("Start"), end ? foreign : target.GetEvent("Done"));
        await b.RestartAsync();
        try
        {
            (await Next(probe)).Should().Be("start");
            FSMEventSharing.Connect(a.Execution, b.Execution);
            a.Execution.PublishEvent("Ping");
            a.Execution.PublishEvent("Fence");
            (await Next(probe)).Should().Be("Fence");
            b.State.Should().Be(FSMState.Running);
        }
        finally { await a.StopAsync(); await b.StopAsync(); }
    }

    private sealed class ReceiverLease(bool throwOnDispose = false) : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose()
        {
            Disposed = true;
            if (throwOnDispose) throw new InvalidOperationException("receiver rollback failure");
        }
    }

    [Fact]
    public async Task ReceiverPreparationFailureReleasesAllTemporaryResourcesAndRetainsRetryableOwnership()
    {
        var ea = Engine();
        var eb = Engine();
        var ec = Engine();
        var inputs = new[] { new Input(), new Input(), new Input() };
        using var pa = ea.BindContext<Input>("Start", "Done").Prepare(inputs[0]);
        using var pb = eb.BindContext<Input>("Start", "Done").Prepare(inputs[1]);
        using var pc = ec.BindContext<Input>("Start", "Done").Prepare(inputs[2]);
        var leaseA = new ReceiverLease(true);
        var leaseB = new ReceiverLease();
        pa.ReceiverPreparation = () => leaseA;
        pb.ReceiverPreparation = () => leaseB;
        pc.ReceiverPreparation = () => throw new InvalidOperationException("receiver preparation failure");
        FSMEventSharing.Connect(pa, pb, pc);
        Func<Task> fail = async () => await FSMEventSharing.StartAsync(pa, pb, pc);
        await fail.Should().ThrowAsync<Exception>();
        leaseA.Disposed.Should().BeTrue();
        leaseB.Disposed.Should().BeTrue();
        inputs.Should().OnlyContain(input => input.Probe.Starts == 0);
        Action conflicting = () => ea.BindContext<Input>("Start", "Done").Prepare(new Input());
        conflicting.Should().Throw<FSMException>();
        pa.ReceiverPreparation = pb.ReceiverPreparation = pc.ReceiverPreparation = null;
        var executors = await FSMEventSharing.StartAsync(pa, pb, pc);
        try
        {
            foreach (var input in inputs) (await Next(input.Probe)).Should().Be("start");
            executors[0].Execution.PublishEvent("Ping");
            foreach (var input in inputs) (await Next(input.Probe)).Should().Be("Ping");
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task AllReceiversAreReadyBeforeInitialNodesPublishAcrossIndependentLocalEventIds()
    {
        var a = new Input { Probe = new Probe { PublishInitially = true } };
        var b = new Input();
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = Engine().BindContext<Input>("Start", "Done").Prepare(b);
        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        try
        {
            (await Next(b.Probe)).Should().Be("start");
            (await Next(b.Probe)).Should().Be("Ping");
            a.Probe.Starts.Should().Be(1);
            b.Probe.Starts.Should().Be(1);
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }
}
