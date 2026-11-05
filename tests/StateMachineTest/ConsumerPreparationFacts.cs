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
