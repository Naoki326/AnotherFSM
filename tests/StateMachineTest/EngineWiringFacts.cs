using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace StateMachineTest;

/// <summary>
/// 特征化测试：锁住"引擎把自身引用注入节点"这条接线行为。
/// 这是 IFSMEngineInjectable 重构的安全网——重构前后这条行为都应保持为绿。
/// </summary>
public class EngineWiringFacts
{
    private sealed class BackendNode(List<(string Name, FSMEngine? Engine)> visits) : EngineBackendNode
    {
        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            visits.Add((Name, Backend));
            yield return new YieldEvent(1);
            await Task.CompletedTask;
        }
    }

    private sealed class BackendFactory(List<(string Name, FSMEngine? Engine)> visits) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => key == "Backend" ? new BackendNode(visits) : throw new ArgumentException(key);
        public Type GetNodeType(string key) => typeof(BackendNode);
        public string GetNodeFeatureName(Type type) => "Backend";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryBackendNodesInFlattenedModulesReceiveTheirEngineAndReallyExecute(bool fluent)
    {
        var visits = new List<(string Name, FSMEngine? Engine)>();
        var factory = new BackendFactory(visits);
        const string module = "module Work { input Entry; output Done; def Entry(Backend) { 1->Done; } }";
        FSMEngine engine;
        if (fluent)
            engine = FSMEngineBuilder.Create(factory).ConfigureFSMDefine(builder => builder
                .RegisterModule("Work", module).AddModule("Work", "A")
                .AddNode("Finish", "Backend", node => node.SetEventBinding(1, "Finished"))
                .AddConnection("A.Done", "A.Entry", "Finish")).Build();
        else
        {
            engine = new FSMEngine(factory);
            engine.CreateStateMachine(module + "use Work as A; def Finish(Backend) { 1->Finished; } A.Done -> Finish;");
        }
        using var executor = await engine.Prepare().StartAsync("A.Entry", "Finished", true);
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        executor.State.Should().Be(FSMState.Finished);
        visits.Select(visit => visit.Name).Should().Equal("A.Entry", "Finish");
        visits.Should().OnlyContain(visit => ReferenceEquals(visit.Engine, engine));
        engine.GetEventNames().Should().NotContain("Done");
        engine.GetNode("A.Entry").UpdateEventDescriptions();
        engine.GetNode("A.Entry").EventDescriptions.Should().Contain(branch => branch.Index == 1 && branch.Description == "A.Done");
    }

    [Fact]
    public void ReinitGroupNode_WiresEngineIntoCreatedNode()
    {
        // Arrange —— 造一个会真造节点的 engine，并放一个节点进去
        var engine = new FSMEngine(new FakeNodeFactory());
        engine.CreateNode("FakeType", "N1");

        // Act —— 历史命名的公开入口仍为普通节点接线
        engine.ReinitGroupNode();

        // Assert —— 该节点拿到了 engine 引用（且就是同一个 engine）
        engine.GetNode("N1")
            .Should().BeOfType<FakeNode>()
            .Which.ReceivedEngine.Should().BeSameAs(engine);
    }
}
