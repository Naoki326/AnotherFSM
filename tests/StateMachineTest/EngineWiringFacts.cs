using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace StateMachineTest;

/// <summary>
/// 特征化测试：锁住"引擎把自身引用注入节点"这条接线行为。
/// 这是 IFSMEngineInjectable 重构的安全网——重构前后这条行为都应保持为绿。
/// </summary>
public class EngineWiringFacts
{
    [Fact]
    public void ReinitGroupNode_WiresEngineIntoCreatedNode()
    {
        // Arrange —— 造一个会真造节点的 engine，并放一个节点进去
        var engine = new FSMEngine(new FakeNodeFactory());
        engine.CreateNode("FakeType", "N1");

        // Act —— 用【公开】入口触发接线（HandleGroupNode 经 ReinitGroupNode 暴露）
        engine.ReinitGroupNode();

        // Assert —— 该节点拿到了 engine 引用（且就是同一个 engine）
        engine.GetNode("N1")
            .Should().BeOfType<FakeNode>()
            .Which.ReceivedEngine.Should().BeSameAs(engine);
    }
}
