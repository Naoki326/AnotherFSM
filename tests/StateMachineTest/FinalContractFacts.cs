using StateMachine;

namespace StateMachineTest;

public class FinalContractFacts
{
    private sealed class Idle : BaseFSMNode
    {
        public int Runs;
        protected override Task ExecuteAsync() { Runs++; PublishEvent(1); return Task.CompletedTask; }
    }
    private sealed class Required : BaseFSMNode<IDisposable>
    {
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }
    private sealed class Factory : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => key == "Required" ? new Required() : new Idle();
        public Type GetNodeType(string key) => key == "Required" ? typeof(Required) : typeof(Idle);
        public string GetNodeFeatureName(Type type) => type.Name;
    }

    [Fact]
    public async Task EnginePublishingIsNodeScopedNotAnAliasForTheCurrentExecution()
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateNode("Idle", "Start");
        engine.AddEvent(new FSMEvent("Done"));
        Action unscoped = () => engine.PublishEvent("Done");
        Action unscopedPayload = () => engine.PublishEvent("Done", "payload");
        for (var round = 0; round < 2; round++)
        {
            using var prepared = engine.Prepare();
            unscoped.Should().Throw<FSMException>().WithMessage("*concrete execution handle*");
            using var executor = await prepared.StartAsync("Start", "Done");
            unscopedPayload.Should().Throw<FSMException>().WithMessage("*concrete execution handle*");
            executor.Execution.Completion.IsCompleted.Should().BeFalse();
            executor.Execution.PublishEvent("Done");
            await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void DependencyFreePreparationNeverTreatsMissingBusinessAsExemption()
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateNode("Required", "Area.Work");
        Action prepare = () => engine.Prepare();
        prepare.Should().Throw<FSMException>().WithMessage("*Area.Work*IDisposable*BindContext*");
        engine.HasUnexitedExecution.Should().BeFalse();
    }

    [Fact]
    public async Task DependencyFreePreparationNeedsNoBusinessInstanceAndStillOwnsTheEngine()
    {
        var engine = new FSMEngine(new Factory());
        engine.CreateNode("Idle", "Start");
        var node = (Idle)engine.GetNode("Start");
        engine.AttachEvent("Start", "Done", 1);
        using var prepared = engine.Prepare();
        node.Runs.Should().Be(0);
        Action conflict = () => engine.Prepare();
        conflict.Should().Throw<FSMException>();
        using var execution = await prepared.StartAsync("Start", "Done");
        await execution.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        node.Runs.Should().Be(1);
        using var next = engine.Prepare();
    }
}
