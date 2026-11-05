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
    private static FSMEngine Engine()
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateNode("Probe", "Start");
        foreach (var name in new[] { "Ping", "Fence", "Done" }) engine.AddEvent(new FSMEvent(name));
        engine.AttachEvent("Start", "Ping", 1);
        engine.ConnectNode("Ping", "Start", "Start");
        engine.ConnectNode("Fence", "Start", "Start");
        return engine;
    }
    private static async Task<string> Next(Probe probe)
        => await probe.Visits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

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
