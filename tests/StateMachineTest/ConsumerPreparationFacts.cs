using StateMachine;

namespace StateMachineTest;

public class ConsumerPreparationFacts
{
    private sealed class Node : BaseFSMNode<IBusiness>
    {
        protected override Task ExecuteAsync()
        {
            Context.Count++;
            PublishEvent(1);
            return Task.CompletedTask;
        }
    }
    public interface IBusiness { int Count { get; set; } }
    public sealed class Business : IBusiness { public int Count { get; set; } }
    public sealed class Input { public Business Job { get; init; } = new(); }
    private sealed class Factory : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new Node();
        public Type GetNodeType(string key) => typeof(Node);
        public string GetNodeFeatureName(Type type) => "Work";
    }

    [Fact]
    public async Task HostObserverFailureKeepsPreparationRetryableAtTheSharedReceiverBarrier()
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateStateMachine("def Work(Work) {1->Error;} def Done event;");
        var input = new Input();
        using var prepared = engine.BindContext<Input>().Prepare(input);
        Action fail = () => prepared.StartAsync("Work", new[] { "Done", "Error" }, _ => throw new InvalidOperationException("observer setup failed"));
        fail.Should().Throw<FSMException>().Which.GetBaseException().Message.Should().Be("observer setup failed");
        input.Job.Count.Should().Be(0);
        engine.HasUnexitedExecution.Should().BeTrue();
        using var executor = await prepared.StartAsync("Work", new[] { "Done", "Error" });
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        input.Job.Count.Should().Be(1);
        engine.HasUnexitedExecution.Should().BeFalse();
    }

    [Fact]
    public async Task HostMultiEndStartCannotBypassTheCompletePendingReceiverSet()
    {
        var first = new FSMEngine(new Factory());
        var second = new FSMEngine(new Factory());
        foreach (var engine in new[] { first, second })
            engine.CreateStateMachine("def Work(Work) {1->Error;} def Done event;");
        var input = new Input();
        var otherInput = new Input();
        using var prepared = first.BindContext<Input>().Prepare(input);
        using var other = second.BindContext<Input>("Work", "Error").Prepare(otherInput);
        FSMEventSharing.Connect(prepared, other);
        var observerInstalled = false;
        Action incomplete = () => prepared.StartAsync("Work", new[] { "Done", "Error" }, _ => observerInstalled = true);
        incomplete.Should().Throw<FSMException>().WithMessage("*every pending*");
        input.Job.Count.Should().Be(0);
        otherInput.Job.Count.Should().Be(0);
        observerInstalled.Should().BeFalse();
        var executions = await FSMEventSharing.StartAsync(prepared, other);
        await Task.WhenAll(executions.Select(e => e.Execution.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
        // A peer's early Error may finish the other receiver before its first node.
        (input.Job.Count + otherInput.Job.Count).Should().BeInRange(1, 2);
        observerInstalled.Should().BeTrue();
        foreach (var execution in executions) execution.Dispose();
    }

    [Fact]
    public async Task TypedConsumerCanObserveFirstNodeAndPreserveMultipleEndEvents()
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateStateMachine("def Work(Work) {1->Error;} def Done event;");
        var input = new Input();
        var activated = new List<string>();
        using var prepared = engine.BindContext<Input>().Prepare(input);
        using var executor = await prepared.StartAsync("Work", new[] { "Done", "Error" },
            executor => executor.NodeStateChanged += (_, name) => activated.Add(name));
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        input.Job.Count.Should().Be(1);
        activated.Should().Equal("Work");
        executor.State.Should().Be(FSMState.Finished);
    }
}
