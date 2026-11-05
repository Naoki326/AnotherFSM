using FSMDemo.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using StateMachine;

namespace FSMDemoApiTest;

public class StartupRetirementFacts
{
    private sealed class Probe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed;
        public int Runs;
        public Action? OnCancel;
        public void ReadHostFromCancellation(EngineManager host)
            => Task.Run(host.GetExecutionStatus).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        public Exception? InitializationError;
        public bool BlockBeforeRound;
        public TaskCompletionSource BeforeRound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PublishRound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void AssignContext()
        {
            if (!BlockBeforeRound) return;
            BeforeRound.TrySetResult();
            PublishRound.Task.GetAwaiter().GetResult();
        }
        public void Initialize(FSMExecutionControl control)
        {
            control.Token.Register(() => { Cancelled.TrySetResult(); OnCancel?.Invoke(); });
            Entered.TrySetResult();
            // Cancellation must reach initialization before startup can finish.
            Cancelled.Task.GetAwaiter().GetResult();
            Release.Task.GetAwaiter().GetResult();
            if (InitializationError != null) throw InitializationError;
        }
    }

    private sealed class InitNode(Probe probe) : BaseFSMNode, IFSMNode, IFSMContextNode
    {
        Type? IFSMContextNode.ContextType => null;
        FSMExecutionControl IFSMNode.Control { get { probe.AssignContext(); return Control; } }
        public override void InitBeforeStart() => probe.Initialize(Control);
        protected override Task ExecuteAsync() { ++probe.Runs; return Task.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) probe.Disposed = true; base.Dispose(disposing); }
    }

    private sealed class TypedInitNode(Probe probe) : BaseFSMNode<DemoNodes.IQueryBusiness>, IFSMNode
    {
        FSMExecutionControl IFSMNode.Control { get { probe.AssignContext(); return Control; } }
        public override void InitBeforeStart() => probe.Initialize(Control);
        protected override Task ExecuteAsync() { ++probe.Runs; return Task.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) probe.Disposed = true; base.Dispose(disposing); }
    }

    private sealed class BorrowedInput(Action? read = null) : IDisposable
    {
        private readonly DemoNodes.DemoJob job = new();
        public DemoNodes.DemoJob Job { get { read?.Invoke(); return job; } }
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private sealed class Factory(Probe probe, bool typed = false) : IFSMNodeFactory
    {
        private readonly BrowserNodeFactory browser = new();
        public IFSMNode CreateNode(string key) => key == "Init" ? typed ? new TypedInitNode(probe) : new InitNode(probe) : browser.CreateNode(key);
        public Type GetNodeType(string key) => key == "Init" ? typed ? typeof(TypedInitNode) : typeof(InitNode) : browser.GetNodeType(key);
        public string GetNodeFeatureName(Type type) => type == typeof(InitNode) || type == typeof(TypedInitNode) ? "Init" : browser.GetNodeFeatureName(type);
    }

    [Theory]
    [InlineData(false, "stop", false)]
    [InlineData(true, "stop", false)]
    [InlineData(false, "import", false)]
    [InlineData(true, "import", false)]
    [InlineData(false, "dispose", false)]
    [InlineData(true, "dispose", false)]
    [InlineData(false, "stop", true)]
    [InlineData(true, "stop", true)]
    [InlineData(false, "import", true)]
    [InlineData(true, "import", true)]
    [InlineData(false, "dispose", true)]
    [InlineData(true, "dispose", true)]
    public async Task RetirementCancelsInitializationBeforeWaitingForStartupAndTrueExit(bool typed, string operation, bool nonStart)
    {
        var probe = new Probe();
        var input = new BorrowedInput();
        await using var host = new EngineManager(NullLogger<EngineManager>.Instance, new Factory(probe, typed),
            context: typed ? HostContext.Create(() => input) : null);
        await host.ImportAsync(nonStart
            ? "def Start(End) {1->Go;} def Next(Init) {1->Done;} Go -> Start to Next;"
            : "def Start(Init) {1->Done;}");
        var graph = host.Engine;
        var starting = Task.Run(() => host.StartExecution("Start", "Done"));
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old = host.Executor!;
        // A cancellation callback must be able to read the host from another thread.
        probe.OnCancel = () => probe.ReadHostFromCancellation(host);
        Task stopping = operation switch
        {
            "import" => host.ImportAsync("def New(End) {1->Done;}"),
            "dispose" => host.DisposeAsync().AsTask(),
            _ => host.StopExecution(),
        };
        try
        {
            await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            stopping.IsCompleted.Should().BeFalse();
            old.Execution.Completion.IsCompleted.Should().BeFalse();
            graph.HasUnexitedExecution.Should().BeTrue();
            probe.Disposed.Should().BeFalse();
            input.Disposed.Should().BeFalse();
            if (operation != "dispose")
            {
                Func<Task> conflict = () => host.StartExecution(operation == "import" ? "New" : "Start", "Done");
                await conflict.Should().ThrowAsync<FSMException>();
            }
        }
        finally { probe.Cancelled.TrySetResult(); probe.Release.TrySetResult(); }
        (await starting.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        probe.Runs.Should().Be(0);
        graph.HasUnexitedExecution.Should().BeFalse();
        input.Disposed.Should().BeFalse();
        probe.Disposed.Should().Be(operation != "stop");
        old.StopException.Should().BeNull();
    }

    // Control thread scheduling, not internal host state: execute the first yielded
    // retirement continuation while startup remains inside the public node setter.
    private sealed class RetirementYield : SynchronizationContext
    {
        private readonly TaskCompletionSource<(SendOrPostCallback Callback, object? State)> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (Interlocked.Increment(ref posts) == 1) first.TrySetResult((callback, state));
            else base.Post(callback, state);
        }
        public async Task RunFirstAsync()
        {
            var work = await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Run(() => work.Callback(work.State)).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetirementBeforeRoundPublicationPreventsFutureInitialization(bool typed)
    {
        var probe = new Probe { BlockBeforeRound = true };
        await using var host = new EngineManager(NullLogger<EngineManager>.Instance, new Factory(probe, typed),
            context: typed ? HostContext.Create(() => new BorrowedInput()) : null);
        await host.ImportAsync("def Start(Init) {1->Done;}");
        var starting = Task.Run(() => host.StartExecution("Start", "Done"));
        await probe.BeforeRound.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old = host.Executor!;
        var schedule = new RetirementYield();
        var previous = SynchronizationContext.Current;
        Task<bool> stopping;
        try
        {
            SynchronizationContext.SetSynchronizationContext(schedule);
            stopping = host.StopExecution();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        try
        {
            await schedule.RunFirstAsync();
            stopping.IsCompleted.Should().BeFalse();
            probe.Disposed.Should().BeFalse();
            probe.PublishRound.TrySetResult();
            (await starting.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
            await old.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            probe.PublishRound.TrySetResult();
            probe.Cancelled.TrySetResult();
            probe.Release.TrySetResult();
        }
        (await starting.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        probe.Runs.Should().Be(0);
        probe.Entered.Task.IsCompleted.Should().BeFalse("retirement closed admission before initialization began");
        host.Engine.HasUnexitedExecution.Should().BeFalse();
    }

    [Theory]
    [InlineData("stop", false)]
    [InlineData("import", false)]
    [InlineData("dispose", false)]
    [InlineData("stop", true)]
    [InlineData("import", true)]
    [InlineData("dispose", true)]
    public async Task RetirementBeforeExecutorExistsRetainsStartupBarrierAndBorrowedResources(string operation, bool inPrepare)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Wait() { entered.TrySetResult(); release.Wait(); }
        var input = new BorrowedInput(inPrepare ? Wait : null);
        var probe = new Probe();
        await using var host = new EngineManager(NullLogger<EngineManager>.Instance, new Factory(probe, true),
            context: HostContext.Create(() => { if (!inPrepare) Wait(); return input; }));
        await host.ImportAsync("def Start(Init) {1->Done;}");
        var graph = host.Engine;
        var starting = Task.Run(() => host.StartExecution("Start", "Done"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Executor.Should().BeNull();
        Task retiring = operation switch
        {
            "import" => host.ImportAsync("def New(End) {1->Done;}"),
            "dispose" => host.DisposeAsync().AsTask(),
            _ => host.StopExecution(),
        };
        try
        {
            retiring.IsCompleted.Should().BeFalse();
            graph.HasUnexitedExecution.Should().Be(inPrepare);
            probe.Disposed.Should().BeFalse();
            input.Disposed.Should().BeFalse();
            if (operation != "dispose")
            {
                Func<Task> conflict = () => host.StartExecution(operation == "import" ? "New" : "Start", "Done");
                await conflict.Should().ThrowAsync<FSMException>();
            }
        }
        finally { release.Set(); }
        (await starting.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
        await retiring.WaitAsync(TimeSpan.FromSeconds(5));
        graph.HasUnexitedExecution.Should().BeFalse();
        input.Disposed.Should().BeFalse();
        probe.Disposed.Should().Be(operation != "stop");
        probe.Entered.Task.IsCompleted.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, "initialization")]
    [InlineData(true, "initialization")]
    [InlineData(false, "cancellation")]
    [InlineData(true, "cancellation")]
    [InlineData(false, "disposed")]
    [InlineData(true, "disposed")]
    public async Task RetirementKeepsInitializationAndCancellationFailuresObservableUntilTrueExit(bool typed, string failureKind)
    {
        var initializationFails = failureKind != "cancellation";
        Exception failure = failureKind == "disposed" ? new ObjectDisposedException(nameof(FSMExecutor))
            : new InvalidOperationException(initializationFails ? "initialization failed" : "cancellation failed");
        var probe = new Probe();
        if (initializationFails) probe.InitializationError = failure;
        else probe.OnCancel = () => throw failure;
        await using var host = new EngineManager(NullLogger<EngineManager>.Instance, new Factory(probe, typed),
            context: typed ? HostContext.Create(() => new BorrowedInput()) : null);
        await host.ImportAsync("def Start(Init) {1->Done;}");
        var starting = Task.Run(() => host.StartExecution("Start", "Done"));
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old = host.Executor!;
        var stopping = host.StopExecution();
        try
        {
            await probe.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            stopping.IsCompleted.Should().BeFalse();
            old.Execution.Completion.IsCompleted.Should().BeFalse();
        }
        finally { probe.Cancelled.TrySetResult(); probe.Release.TrySetResult(); }
        if (initializationFails)
        {
            Func<Task> start = async () => await starting.WaitAsync(TimeSpan.FromSeconds(5));
            (await start.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
        }
        else (await starting.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
        Func<Task> completion = async () => await old.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var observed = (await completion.Should().ThrowAsync<Exception>()).Which;
        observed.GetBaseException().Should().BeSameAs(failure);
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        host.Engine.HasUnexitedExecution.Should().BeFalse();
        probe.Runs.Should().Be(0);
    }
}
