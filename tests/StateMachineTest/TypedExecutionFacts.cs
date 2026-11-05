using System.Collections;
using StateMachine;

namespace StateMachineTest;

public class TypedExecutionFacts
{
    public interface IBusiness { int Count { get; set; } }
    public sealed class Business : IBusiness, IDisposable
    {
        public int Count { get; set; }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    public sealed class Input { public Business Business { get; init; } = new(); }
    private sealed class Producer(List<FSMEvent?> triggers) : AsyncEnumFSMNode<IBusiness>
    {
        public override void InitBeforeStart() => triggers.Add(TriggerEvent);
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            triggers.Add(TriggerEvent);
            Context.Count++;
            Control.CheckPause();
            yield return Yield.Event(1, "payload");
            await Task.CompletedTask;
        }
    }
    private sealed class PausingConsumer(List<(int Count, FSMEvent? Trigger)> observations) : EnumFSMNode<IBusiness>
    {
        private int privateVisits;
        public override void InitBeforeStart() => privateVisits = 0;
        protected override IEnumerable ExecuteEnumerable()
        {
            privateVisits++;
            observations.Add((Context.Count + privateVisits, TriggerEvent));
            yield return Yield.Pause;
            observations.Add((Context.Count + privateVisits, TriggerEvent));
            yield return Yield.Event(1);
        }
    }
    private sealed class Factory(List<FSMEvent?> triggers, List<(int Count, FSMEvent? Trigger)> observations) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => key == "Producer" ? new Producer(triggers) : new PausingConsumer(observations);
        public Type GetNodeType(string key) => key == "Producer" ? typeof(Producer) : typeof(PausingConsumer);
        public string GetNodeFeatureName(Type type) => type == typeof(Producer) ? "Producer" : "Consumer";
    }
    private static async Task WaitForState(FSMExecutor executor, FSMState expected)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(FSMExecutor _, FSMState current, FSMState previous) { if (current == expected) reached.TrySetResult(); }
        executor.FSMStateChanged += Changed;
        try
        {
            if (executor.State == expected) reached.TrySetResult();
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { executor.FSMStateChanged -= Changed; }
    }

    public sealed class EmptyInput { }
    private sealed class PlainBase(List<string> observations) : BaseFSMNode
    {
        protected override Task ExecuteAsync()
        {
            observations.Add(TriggerEvent == null ? "base:fresh" : "base:stale");
            Control.CheckPause();
            PublishEvent(1, "base payload");
            return Task.CompletedTask;
        }
    }
    private sealed class PlainEnum(List<string> observations) : EnumFSMNode
    {
        protected override IEnumerable ExecuteEnumerable()
        {
            observations.Add($"enum:{TriggerEvent?.EventContext}");
            yield return Yield.Pause;
            observations.Add($"continued:{TriggerEvent?.EventContext}");
            yield return Yield.None;
            yield return Yield.Event(1, "enum payload");
        }
    }
    private sealed class PlainAsync(List<string> observations) : AsyncEnumFSMNode
    {
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            observations.Add($"async:{TriggerEvent?.EventContext}");
            yield return Yield.Delay(1);
            yield return Yield.Event(1);
            await Task.CompletedTask;
        }
    }
    private sealed class PlainFactory(List<string> observations) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => key switch
        {
            "Base" => new PlainBase(observations), "Enum" => new PlainEnum(observations), _ => new PlainAsync(observations)
        };
        public Type GetNodeType(string key) => key switch
        {
            "Base" => typeof(PlainBase), "Enum" => typeof(PlainEnum), _ => typeof(PlainAsync)
        };
        public string GetNodeFeatureName(Type type) => throw new NotSupportedException();
    }

    [Fact]
    public async Task NonGenericFamiliesDeclareNoDependencyAndYieldControlsDoNotNeedAnEmptyBusinessObject()
    {
        var observations = new List<string>();
        var engine = new FSMEngine(new PlainFactory(observations));
        foreach (var key in new[] { "Base", "Enum", "Async" }) engine.CreateNode(key, key);
        engine.AttachEvent("Base", "ToEnum", 1);
        engine.AttachEvent("Enum", "ToAsync", 1);
        engine.AttachEvent("Async", "Done", 1);
        engine.ConnectNode("ToEnum", "Base", "Enum");
        engine.ConnectNode("ToAsync", "Enum", "Async");
        using var executor = await engine.BindContext<EmptyInput>().Prepare(new EmptyInput()).StartAsync("Base", "Done");
        await WaitForState(executor, FSMState.Paused);
        try { await executor.CurrentNodeTask; } catch (OperationCanceledException) { }
        executor.Continue().Should().BeTrue();
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        observations.Should().Equal("base:fresh", "enum:base payload", "continued:base payload", "async:enum payload");
    }

    private sealed class LoopNode(Action cleanup) : BaseFSMNode<IBusiness>
    {
        protected override Task ExecuteAsync()
        {
            try
            {
                Context.Count++;
                PublishEvent(Context.Count < 2 ? 1 : 2);
                return Task.CompletedTask;
            }
            finally { cleanup(); } // the node owns its temporary work, not the caller's Business
        }
    }
    private sealed class LoopFactory(Action cleanup) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new LoopNode(cleanup);
        public Type GetNodeType(string key) => typeof(LoopNode);
        public string GetNodeFeatureName(Type type) => "Loop";
    }

    [Fact]
    public async Task LoopReentryKeepsBusinessMutationsAndFrameworkDoesNotDisposeCallerResources()
    {
        var cleanups = 0;
        var engine = new FSMEngine(new LoopFactory(() => cleanups++));
        engine.CreateNode("Loop", "Loop");
        engine.AttachEvent("Loop", "Again", 1);
        engine.AttachEvent("Loop", "Done", 2);
        engine.ConnectNode("Again", "Loop", "Loop");
        var input = new Input();
        using var executor = await engine.BindContext<Input>().Prepare(input).StartAsync("Loop", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        await executor.StopAsync();
        input.Business.Count.Should().Be(2);
        input.Business.Disposed.Should().BeFalse();
        cleanups.Should().Be(2);
    }

    [Fact]
    public async Task YieldPauseContinuePreservesBusinessAndTriggerWhileNewExecutionInitializesPrivateState()
    {
        var triggers = new List<FSMEvent?>();
        var observations = new List<(int Count, FSMEvent? Trigger)>();
        var engine = new FSMEngine(new Factory(triggers, observations));
        engine.CreateNode("Producer", "Producer");
        engine.CreateNode("Consumer", "Consumer");
        engine.AttachEvent("Producer", "Next", 1);
        engine.AttachEvent("Consumer", "Done", 1);
        engine.ConnectNode("Next", "Producer", "Consumer");
        var flow = engine.BindContext<Input>();
        var input = new Input();
        for (var run = 0; run < 2; run++)
        {
            using var executor = await flow.Prepare(input).StartAsync("Producer", "Done");
            await WaitForState(executor, FSMState.Paused);
            try { await executor.CurrentNodeTask; } catch (OperationCanceledException) { }
            input.Business.Count.Should().Be(run + 1);
            executor.Continue().Should().BeTrue();
            await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        observations.Select(o => o.Count).Should().Equal(2, 2, 3, 3);
        observations.Select(o => o.Trigger!.EventName).Should().OnlyContain(name => name == "Next");
        observations.Select(o => o.Trigger!.EventContext).Should().OnlyContain(payload => Equals(payload, "payload"));
        observations[0].Trigger.Should().BeSameAs(observations[1].Trigger);
        triggers.Should().OnlyContain(trigger => trigger == null);
        input.Business.Disposed.Should().BeFalse();
    }
}
