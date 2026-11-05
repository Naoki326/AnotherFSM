using System.Text.Json;
using FSMDemo.Runtime;
using StateMachine;

namespace FSMDemoWasmTest;

// Shared JSON protocol evidence; the real Release WASM/API browser suite runs separately.
public class StartupRetirementSessionFacts
{
    private sealed class Probe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Runs;
        public bool Disposed;
        public void Initialize(FSMExecutionControl control)
        {
            control.Token.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            Cancelled.Task.GetAwaiter().GetResult();
            Release.Task.GetAwaiter().GetResult();
        }
    }
    private sealed class PlainNode(Probe probe) : BaseFSMNode
    {
        public override void InitBeforeStart() => probe.Initialize(Control);
        protected override Task ExecuteAsync() { ++probe.Runs; return Task.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) probe.Disposed = true; base.Dispose(disposing); }
    }
    private sealed class TypedNode(Probe probe) : BaseFSMNode<DemoNodes.IQueryBusiness>
    {
        public override void InitBeforeStart() => probe.Initialize(Control);
        protected override Task ExecuteAsync() { ++probe.Runs; return Task.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) probe.Disposed = true; base.Dispose(disposing); }
    }
    private sealed class Factory(Probe probe, bool typed) : IFSMNodeFactory
    {
        private readonly BrowserNodeFactory browser = new();
        public IFSMNode CreateNode(string key) => key == "Init" ? typed ? new TypedNode(probe) : new PlainNode(probe) : browser.CreateNode(key);
        public Type GetNodeType(string key) => key == "Init" ? typed ? typeof(TypedNode) : typeof(PlainNode) : browser.GetNodeType(key);
        public string GetNodeFeatureName(Type type) => type == typeof(TypedNode) || type == typeof(PlainNode) ? "Init" : browser.GetNodeFeatureName(type);
    }

    [Theory]
    [InlineData(false, "stopExecution")]
    [InlineData(true, "stopExecution")]
    [InlineData(false, "importScript")]
    [InlineData(true, "importScript")]
    [InlineData(false, "dispose")]
    [InlineData(true, "dispose")]
    public async Task JsonRetirementCancelsInitializationAndWaitsForItsExit(bool typed, string operation)
    {
        var probe = new Probe();
        var activated = 0;
        await using var session = new RuntimeSession(new Factory(probe, typed), json =>
        {
            using var notification = JsonDocument.Parse(json);
            if (notification.RootElement.GetProperty("type").GetString() == "NodeActivated") Interlocked.Increment(ref activated);
        }, context: typed ? HostContext.Create(() => new DemoNodes.DemoBusinessInput()) : null);
        using var imported = JsonDocument.Parse(await session.InvokeAsync("importScript", "{\"script\":\"def Start(Init) {1->Done;}\"}"));
        imported.RootElement.TryGetProperty("error", out _).Should().BeFalse();
        const string start = "{\"startNodeName\":\"Start\",\"endEventName\":\"Done\"}";
        var starting = Task.Run(() => session.InvokeAsync("startExecution", start));
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task retiring = operation == "dispose" ? session.DisposeAsync().AsTask()
            : session.InvokeAsync(operation, operation == "importScript" ? "{\"script\":\"def New(End) {1->Done;}\"}" : "{}");
        try
        {
            await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            retiring.IsCompleted.Should().BeFalse();
            probe.Disposed.Should().BeFalse();
            if (operation != "dispose")
            {
                using var rejected = JsonDocument.Parse(await session.InvokeAsync("startExecution", start));
                rejected.RootElement.GetProperty("error").GetString().Should().Contain("unexited");
            }
        }
        finally { probe.Cancelled.TrySetResult(); probe.Release.TrySetResult(); }
        using var abandoned = JsonDocument.Parse(await starting.WaitAsync(TimeSpan.FromSeconds(5)));
        abandoned.RootElement.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
        await retiring.WaitAsync(TimeSpan.FromSeconds(5));
        probe.Runs.Should().Be(0);
        activated.Should().Be(0);
        probe.Disposed.Should().Be(operation != "stopExecution");
    }
}
