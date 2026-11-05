using System.Threading.Channels;
using StateMachine;

namespace StateMachineTest;

public class EventSharingFacts
{
    public sealed class Probe
    {
        public Channel<string> Visits { get; } = Channel.CreateUnbounded<string>();
        public Channel<object?> Payloads { get; } = Channel.CreateUnbounded<object?>();
        public bool PublishInitially { get; init; }
        public Task? InitializationGate { get; init; }
        public Task? PublicationGate { get; init; }
        public int Starts;
        public TaskCompletionSource InitialPublished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool EchoOnce { get; init; }
        public int Echoes;
        public TaskCompletionSource EchoPublished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ReturnGate { get; init; }
        public bool PublishAfterGate { get; init; }
        public LatePublish? Late { get; init; }
    }
    public sealed class LatePublish
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Input { public Probe Probe { get; init; } = new(); }
    private sealed class Node : BaseFSMNode<Probe>
    {
        // Public synchronous initialization hook: hold the target before its first Run.
        public override void InitBeforeStart()
            => Context.InitializationGate?.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        protected override async Task ExecuteAsync()
        {
            Context.Visits.Writer.TryWrite(TriggerEvent?.EventName ?? "start");
            Context.Payloads.Writer.TryWrite(TriggerEvent?.EventContext);
            if (TriggerEvent == null && Interlocked.Increment(ref Context.Starts) == 1)
            {
                if (Context.Late != null) _ = SendLate(Context.Late);
                if (Context.PublishInitially)
                {
                    if (Context.PublicationGate != null) await Context.PublicationGate.WaitAsync(TimeSpan.FromSeconds(5));
                    PublishEvent(1);
                }
            }
            if (TriggerEvent == null) Context.InitialPublished.TrySetResult();
            if (Context.ReturnGate != null) await Context.ReturnGate.Task;
            if (Context.PublishAfterGate) PublishEvent(1);
            if (TriggerEvent?.EventName == "Ping" && Context.EchoOnce && Interlocked.Increment(ref Context.Echoes) == 1)
            {
                PublishEvent(1);
                Context.EchoPublished.TrySetResult();
            }
        }
        private async Task SendLate(LatePublish late)
        {
            await late.Release.Task;
            PublishEvent(1);
            late.Sent.TrySetResult();
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

    [Theory]
    [InlineData("CheckA", false)]
    [InlineData("CheckB", false)]
    [InlineData("checka", false)]
    [InlineData("bare", false)]
    [InlineData("CheckA", true)]
    [InlineData("CheckB", true)]
    [InlineData("checka", true)]
    [InlineData("bare", true)]
    public async Task RealModuleOutputsKeepTheirFullCaseSensitiveLocalIdentityForOrdinaryAndEndEvents(string instance, bool end)
    {
        const string module = "module Work { input Entry; output OK; def Entry(Probe) { 1->OK; } }";
        var source = new FSMEngine(new Factory());
        source.CreateStateMachine(module + "use Work as CheckA;");
        source.AddEvent(new FSMEvent("Done"));
        source.AddEvent(new FSMEvent("Fence"));
        source.ConnectNode("Fence", "CheckA.Entry", "CheckA.Entry");
        var target = new FSMEngine(new Factory());
        var targetName = instance == "bare" ? "OK" : instance + ".OK";
        var targetNode = instance == "bare" ? "Start" : instance + ".Entry";
        target.CreateStateMachine(instance == "bare" ? "def Start(Probe) { 1->OK; }" : module + $"use Work as {instance};");
        target.AddEvent(new FSMEvent("Done"));
        target.AddEvent(new FSMEvent("Fence"));
        target.ConnectNode("Fence", targetNode, targetNode);
        target.ConnectNode(targetName, targetNode, targetNode);
        var bInput = new Input();
        // Identity, not initial scheduling, is under test: publish only after target entry.
        var aInput = new Input
        {
            Probe = new Probe { PublishInitially = true, PublicationGate = bInput.Probe.InitialPublished.Task }
        };
        using var pa = source.BindContext<Input>("CheckA.Entry", "Done").Prepare(aInput);
        using var pb = target.BindContext<Input>(targetNode, end ? targetName : "Done").Prepare(bInput);
        FSMEventSharing.Connect(pa, pb);
        var executors = await Task.Run(() => FSMEventSharing.StartAsync(pa, pb));
        try
        {
            await aInput.Probe.InitialPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            (await Next(bInput.Probe)).Should().Be("start");
            if (instance == "CheckA" && end)
            {
                await executors[1].Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
                executors[1].State.Should().Be(FSMState.Finished);
            }
            else
            {
                executors[0].Execution.PublishEvent("Fence");
                if (instance == "CheckA") (await Next(bInput.Probe)).Should().Be("CheckA.OK");
                (await Next(bInput.Probe)).Should().Be("Fence");
                executors[1].State.Should().Be(FSMState.Running);
            }
            source.GetEventNames().Should().NotContain("OK");
            aInput.Probe.Starts.Should().Be(1);
            bInput.Probe.Starts.Should().Be(1);
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task ARealModuleEndReceivedBeforeTheInitialRunFinishesWithoutExecutingAndReleasesOwnership()
    {
        const string module = "module Work { input Entry; output OK; def Entry(Probe) { 1->OK; } } use Work as CheckA;";
        var source = new FSMEngine(new Factory());
        source.CreateStateMachine(module);
        source.AddEvent(new FSMEvent("Done"));
        var target = new FSMEngine(new Factory());
        target.CreateStateMachine(module);
        var aInput = new Input { Probe = new Probe { PublishInitially = true } };
        var bInput = new Input
        {
            Probe = new Probe { InitializationGate = aInput.Probe.InitialPublished.Task }
        };
        var targetFlow = target.BindContext<Input>("CheckA.Entry", "CheckA.OK");
        using var pa = source.BindContext<Input>("CheckA.Entry", "Done").Prepare(aInput);
        using var pb = targetFlow.Prepare(bInput);
        FSMEventSharing.Connect(pa, pb);
        // InitBeforeStart is synchronous; run the coordinated start off the test context.
        var executors = await Task.Run(() => FSMEventSharing.StartAsync(pa, pb));
        try
        {
            await executors[1].Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            aInput.Probe.InitialPublished.Task.IsCompletedSuccessfully.Should().BeTrue();
            executors[1].State.Should().Be(FSMState.Finished);
            executors[1].Execution.Completion.IsCompletedSuccessfully.Should().BeTrue();
            bInput.Probe.Starts.Should().Be(0);
            bInput.Probe.Visits.Reader.TryRead(out _).Should().BeFalse();

            // Completion, without Stop/Dispose of the old round, releases the engine and
            // bound node for a real new round.
            var nextInput = new Input();
            using var nextPreparation = targetFlow.Prepare(nextInput);
            using var next = await nextPreparation.StartAsync();
            try
            {
                (await Next(nextInput.Probe)).Should().Be("start");
                nextInput.Probe.Starts.Should().Be(1);
                next.Execution.PublishEvent("CheckA.OK");
                await next.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
                next.State.Should().Be(FSMState.Finished);
                executors[0].State.Should().Be(FSMState.Running);
            }
            finally { await next.StopAsync(); }
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task ARegisteredLocalEndNameDoesNotDependOnTheConfiguredForeignEventId()
    {
        var aInput = new Input();
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(aInput);
        using var a = await pa.StartAsync();
        var probe = new Probe();
        var target = new FSMEngine(new BareFactory(probe));
        target.CreateNode("Bare", "Start");
        target.AddEvent(new FSMEvent("Done"));
        using var b = new FSMExecutor(target.GetNode("Start"), new FSMEvent("Done"));
        await b.RestartAsync();
        try
        {
            (await Next(probe)).Should().Be("start");
            FSMEventSharing.Connect(a.Execution, b.Execution);
            a.Execution.PublishEvent("Done");
            await b.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            b.State.Should().Be(FSMState.Finished);
        }
        finally { await a.StopAsync(); await b.StopAsync(); }
    }

    [Fact]
    public async Task AnActiveBusinessResponseRepublishesTheSameNameAsANewNotification()
    {
        var a = new Input();
        var b = new Input { Probe = new Probe { EchoOnce = true } };
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = Engine().BindContext<Input>("Start", "Done").Prepare(b);
        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        try
        {
            (await Next(a.Probe)).Should().Be("start");
            (await Next(b.Probe)).Should().Be("start");
            executors[0].Execution.PublishEvent("Ping");
            await b.Probe.EchoPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            executors[0].Execution.PublishEvent("Fence");
            foreach (var probe in new[] { a.Probe, b.Probe })
            {
                (await Next(probe)).Should().Be("Ping");
                (await Next(probe)).Should().Be("Ping");
                (await Next(probe)).Should().Be("Fence");
            }
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task PausedMembersQueueOrdinaryAndBusinessEndEventsWithoutControllingOtherMembers()
    {
        var a = new Input();
        var b = new Input();
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = Engine().BindContext<Input>("Start", "Done").Prepare(b);
        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        try
        {
            (await Next(a.Probe)).Should().Be("start");
            (await Next(b.Probe)).Should().Be("start");
            executors[1].Execution.Pause();
            executors[1].State.Should().Be(FSMState.Paused);
            executors[0].State.Should().Be(FSMState.Running);
            executors[0].Execution.PublishEvent("Ping");
            (await Next(a.Probe)).Should().Be("Ping");
            executors[0].Execution.PublishEvent("Done");
            await executors[0].Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            executors[1].State.Should().Be(FSMState.Paused);
            b.Probe.Visits.Reader.TryRead(out _).Should().BeFalse();
            executors[1].Execution.Continue().Should().BeTrue();
            await executors[1].Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            (await Next(b.Probe)).Should().Be("start"); // resume retains the original trigger
            (await Next(b.Probe)).Should().Be("Ping");
            executors[1].State.Should().Be(FSMState.Finished);
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task StoppingRejectsNewEventsButKeepsEngineOwnershipUntilTheNonCooperativeNodeActuallyReturns()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = new Input();
        var b = new Input { Probe = new Probe { ReturnGate = gate, PublishAfterGate = true } };
        var engine = Engine();
        var flow = engine.BindContext<Input>("Start", "Done");
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = flow.Prepare(b);
        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        try
        {
            (await Next(a.Probe)).Should().Be("start");
            (await Next(b.Probe)).Should().Be("start");
            var stopped = executors[1].Execution.StopAsync();
            executors[1].State.Should().Be(FSMState.Stopping);
            stopped.IsCompleted.Should().BeFalse();
            Action replace = () => flow.Prepare(new Input());
            replace.Should().Throw<FSMException>();
            executors[0].Execution.PublishEvent("Ping");
            (await Next(a.Probe)).Should().Be("Ping");
            executors[0].State.Should().Be(FSMState.Running);
            Action publishStopped = () => executors[1].Execution.PublishEvent("Ping");
            publishStopped.Should().Throw<FSMException>();
            gate.TrySetResult();
            await stopped.WaitAsync(TimeSpan.FromSeconds(5));
            executors[0].Execution.PublishEvent("Fence");
            (await Next(a.Probe)).Should().Be("Fence"); // late node publication did not escape stopping
            b.Probe.Visits.Reader.TryRead(out _).Should().BeFalse();
            using var next = flow.Prepare(new Input());
        }
        finally { gate.TrySetResult(); foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task RestartedMembersNeedExplicitConnectionAndOldNodeRoutesCannotPublishIntoTheNewRound()
    {
        var late = new LatePublish();
        var a = new Input();
        var oldInput = new Input { Probe = new Probe { Late = late } };
        var engine = Engine();
        var flow = engine.BindContext<Input>("Start", "Done");
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = flow.Prepare(oldInput);
        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        FSMExecutor? replacement = null;
        try
        {
            (await Next(a.Probe)).Should().Be("start");
            (await Next(oldInput.Probe)).Should().Be("start");
            var oldRound = executors[1].Execution;
            await oldRound.StopAsync();
            var nextInput = new Input();
            using var next = flow.Prepare(nextInput);
            replacement = await next.StartAsync();
            (await Next(nextInput.Probe)).Should().Be("start");
            late.Release.TrySetResult();
            await late.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Action stale = () => oldRound.PublishEvent("Ping");
            stale.Should().Throw<FSMException>();
            Action staleConnect = () => FSMEventSharing.Connect(executors[0].Execution, oldRound);
            staleConnect.Should().Throw<FSMException>();
            executors[0].Execution.PublishEvent("Ping");
            executors[0].Execution.PublishEvent("Fence");
            (await Next(a.Probe)).Should().Be("Ping");
            (await Next(a.Probe)).Should().Be("Fence");
            replacement.Execution.PublishEvent("Fence");
            (await Next(nextInput.Probe)).Should().Be("Fence");
            FSMEventSharing.Connect(executors[0].Execution, replacement.Execution);
            executors[0].Execution.PublishEvent("Ping");
            (await Next(a.Probe)).Should().Be("Ping");
            (await Next(nextInput.Probe)).Should().Be("Ping");
        }
        finally
        {
            late.Release.TrySetResult();
            if (replacement != null) await replacement.StopAsync();
            foreach (var executor in executors) await executor.StopAsync();
        }
    }

    [Fact]
    public async Task ExplicitEntrySelectionWorksForTheSingleExecutionPathAsWellAsCoordinatedStartup()
    {
        var input = new Input();
        using var preparation = Engine().BindContext<Input>().Prepare(input).WithEntry("Start", "Done");
        using var executor = await preparation.StartAsync();
        try { (await Next(input.Probe)).Should().Be("start"); }
        finally { await executor.StopAsync(); }
    }

    [Fact]
    public async Task InitialReceiverBarrierReleasesZeroNodesAndFailedPreparationsCanBeExplicitlyAbandoned()
    {
        var aEngine = Engine();
        var bEngine = Engine();
        var a = new Input { Probe = new Probe { PublishInitially = true } };
        var b = new Input();
        using var pa = aEngine.BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = bEngine.BindContext<Input>("Start", "Done").Prepare(b);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = new ReceiverLease();
        pa.ReceiverPreparation = () => lease;
        pb.ReceiverPreparation = () =>
        {
            ready.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("initial receiver failure");
        };
        FSMEventSharing.Connect(pa, pb);
        var attempt = Task.Run(() => FSMEventSharing.StartAsync(pa, pb));
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            a.Probe.Starts.Should().Be(0);
            b.Probe.Starts.Should().Be(0);
            a.Probe.Visits.Reader.TryRead(out _).Should().BeFalse();
            b.Probe.Visits.Reader.TryRead(out _).Should().BeFalse();
        }
        finally { release.TrySetResult(); }
        Func<Task> failed = async () => await attempt.WaitAsync(TimeSpan.FromSeconds(5));
        await failed.Should().ThrowAsync<FSMException>();
        lease.Disposed.Should().BeTrue();
        pa.Dispose();
        pb.Dispose();
        using var nextA = aEngine.BindContext<Input>("Start", "Done").Prepare(new Input());
        using var nextB = bEngine.BindContext<Input>("Start", "Done").Prepare(new Input());
    }

    public sealed class SwitchableInput
    {
        private Probe value = new();
        public int Reads;
        public Probe Probe { get { Interlocked.Increment(ref Reads); return value; } }
        public void Replace(Probe replacement) => value = replacement;
    }

    [Fact]
    public async Task CoordinatedStartReusesFixedSourcesAndSharedPayloadsNeverReplaceTargetBindingsOrCanonicalEvents()
    {
        var a = new SwitchableInput();
        var b = new SwitchableInput();
        var aProbe = a.Probe;
        var bProbe = b.Probe;
        var aEngine = Engine();
        var bEngine = Engine();
        using var pa = aEngine.BindContext<SwitchableInput>().Prepare(a);
        using var pb = bEngine.BindContext<SwitchableInput>().Prepare(b);
        var readsA = a.Reads;
        var readsB = b.Reads;
        var replacementA = new Probe();
        var replacementB = new Probe();
        a.Replace(replacementA);
        b.Replace(replacementB);
        pa.WithEntry("Start", "Done");
        pb.WithEntry("Start", "Done");
        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        try
        {
            foreach (var probe in new[] { aProbe, bProbe })
            {
                (await Next(probe)).Should().Be("start");
                (await probe.Payloads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull();
            }
            var payload = new object();
            executors[0].Execution.PublishEvent("Ping", payload);
            foreach (var probe in new[] { aProbe, bProbe })
            {
                (await Next(probe)).Should().Be("Ping");
                (await probe.Payloads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().BeSameAs(payload);
            }
            a.Reads.Should().Be(readsA);
            b.Reads.Should().Be(readsB);
            replacementA.Starts.Should().Be(0);
            replacementB.Starts.Should().Be(0);
            aEngine.GetEvent("Ping").EventContext.Should().BeNull();
            bEngine.GetEvent("Ping").EventContext.Should().BeNull();
        }
        finally { foreach (var executor in executors) await executor.StopAsync(); }
    }

    [Fact]
    public async Task ANotificationAlreadyInFlightCannotRetargetAReplacementThatJoinsBeforeDeliveryReturns()
    {
        var aInput = new Input();
        var bInput = new Input();
        var cInput = new Input();
        var bEngine = Engine();
        var cEngine = Engine();
        foreach (var engine in new[] { bEngine, cEngine })
        {
            engine.AddEvent(new FSMEvent("Never"));
            engine.ConnectNode("Done", "Start", "Start");
        }
        var bFlow = bEngine.BindContext<Input>("Start", "Never");
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(aInput);
        using var pb = bFlow.Prepare(bInput);
        using var pc = cEngine.BindContext<Input>("Start", "Never").Prepare(cInput);
        FSMEventSharing.Connect(pa, pb, pc);
        var executors = await FSMEventSharing.StartAsync(pa, pb, pc);
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        executors[0].FSMStateChanged += (_, state, _) =>
        {
            if (state != FSMState.Finished) return;
            publishing.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        };
        FSMExecutor? replacement = null;
        Task? publication = null;
        try
        {
            foreach (var input in new[] { aInput, bInput, cInput }) (await Next(input.Probe)).Should().Be("start");
            publication = Task.Run(() => executors[0].Execution.PublishEvent("Done"));
            await publishing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            executors[0].Execution.Completion.IsCompleted.Should().BeFalse();
            await executors[1].StopAsync();
            var nextInput = new Input();
            using var next = bFlow.Prepare(nextInput);
            FSMEventSharing.Connect(executors[2].Execution, next);
            replacement = (await FSMEventSharing.StartAsync(next))[0];
            (await Next(nextInput.Probe)).Should().Be("start");
            release.TrySetResult();
            await publication.WaitAsync(TimeSpan.FromSeconds(5));
            replacement.Execution.PublishEvent("Fence");
            (await Next(nextInput.Probe)).Should().Be("Fence");
            nextInput.Probe.Starts.Should().Be(1);
        }
        finally
        {
            release.TrySetResult();
            if (publication != null) await publication.WaitAsync(TimeSpan.FromSeconds(5));
            if (replacement != null) await replacement.StopAsync();
            foreach (var executor in executors) await executor.StopAsync();
        }
    }

    [Fact]
    public async Task MissingSelectionDiagnosticsIdentifyThePendingEntryAndEngine()
    {
        var receiver = Engine();
        receiver.CreateNode("Probe", "Receiver");
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(new Input());
        using var pb = receiver.BindContext<Input>("Receiver", "Done").Prepare(new Input());
        FSMEventSharing.Connect(pa, pb);
        Func<Task> incomplete = async () => await FSMEventSharing.StartAsync(pa);
        await incomplete.Should().ThrowAsync<FSMException>().WithMessage("*pending*Receiver*engine*");
    }

    [Fact]
    public async Task SharedEventsQueuedInAPausedOldRoundCannotSurviveStopAndExplicitReconnection()
    {
        var a = new Input();
        var b = new Input();
        var engine = Engine();
        var flow = engine.BindContext<Input>("Start", "Done");
        using var pa = Engine().BindContext<Input>("Start", "Done").Prepare(a);
        using var pb = flow.Prepare(b);
        FSMEventSharing.Connect(pa, pb);
        var executors = await FSMEventSharing.StartAsync(pa, pb);
        FSMExecutor? replacement = null;
        try
        {
            (await Next(a.Probe)).Should().Be("start");
            (await Next(b.Probe)).Should().Be("start");
            executors[1].Execution.Pause();
            executors[0].Execution.PublishEvent("Ping");
            (await Next(a.Probe)).Should().Be("Ping");
            executors[1].State.Should().Be(FSMState.Paused);
            await executors[1].Execution.StopAsync();
            var nextInput = new Input();
            using var next = flow.Prepare(nextInput);
            FSMEventSharing.Connect(executors[0].Execution, next);
            replacement = (await FSMEventSharing.StartAsync(next))[0];
            (await Next(nextInput.Probe)).Should().Be("start");
            executors[0].Execution.PublishEvent("Fence");
            (await Next(nextInput.Probe)).Should().Be("Fence");
            b.Probe.Visits.Reader.TryRead(out _).Should().BeFalse();
        }
        finally
        {
            if (replacement != null) await replacement.StopAsync();
            foreach (var executor in executors) await executor.StopAsync();
        }
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
