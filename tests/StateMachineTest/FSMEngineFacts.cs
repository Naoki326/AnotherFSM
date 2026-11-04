using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace StateMachineTest
{
    public class FSMEngineFacts
    {
        [Fact]
        public void Engine_ContainsNoNode()
        {
            // Arrange
            var engine = new FSMEngine(new ThrowingNodeFactory());

            // Act
            var result = engine as IEnumerable<IFSMNode>;

            // Assert -- 验证可观察的结果
            result.Should().BeEmpty();
        }

        [Fact]
        public void Engine_ContainsNoNodeNames()
        {
            // Arrange
            var engine = new FSMEngine(new ThrowingNodeFactory());

            // Act
            var result = engine.GetNodeNames();

            // Assert -- 验证可观察的结果
            result.Should().BeEmpty();
        }

        [Fact]
        public void ContainsNode_UnkonwnName_ReturnsFalse()
        {
            // Arrange
            var engine = new FSMEngine(new ThrowingNodeFactory());

            // Act
            var result = engine.ContainsNode("Start");

            // Assert -- 验证可观察的结果
            result.Should().BeFalse();
        }

        [Fact]
        public void GetNode_OnMissing_ThrowsKeyNotFound()
        {
            // Arrange
            var engine = new FSMEngine(new ThrowingNodeFactory());

            // Act —— 把会抛的动作包起来
            Action act = () => engine.GetNode("missing");

            // Assert —— 真实契约：抛 KeyNotFoundException，消息含节点名
            act.Should().Throw<KeyNotFoundException>()
                .WithMessage("*missing*");
        }

        [Fact]
        public void ContainsEvent_UnkonwnName_ReturnsFalse()
        {
            // Arrange
            var engine = new FSMEngine(new ThrowingNodeFactory());

            // Act
            var result = engine.ContainsEvent("DoesNotExist");

            // Assert —— 真实契约：抛 KeyNotFoundException，消息含节点名
            result.Should().BeFalse();
        }

        [Fact]
        public void FreshEngine_ContainsNoEventNames()
        {
            // Arrange
            var engine = new FSMEngine(new ThrowingNodeFactory());

            // Act
            var result = engine.GetEventNames();

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void GetEvent_OnMissing_ThrowsKeyNotFound()
        {
            // Arrange
            var engine = new FSMEngine(new ThrowingNodeFactory());

            // Act —— 把会抛的动作包起来
            Action act = () => engine.GetEvent("missing");

            // Assert —— 真实契约：抛 KeyNotFoundException，消息含节点名
            act.Should().Throw<KeyNotFoundException>()
                .WithMessage("*missing*");
        }

        [Fact]
        public void CreateNode_ThenContainsNode_ReturnsTrue()
        {
            // Arrange
            var engine = new FSMEngine(new FakeNodeFactory());

            // Act
            engine.CreateNode("FakeType", "MyNode");

            // Assert
            engine.ContainsNode("MyNode").Should().BeTrue();
        }

        [Fact]
        public void CreateNode_ThenGetNode_NotNull()
        {
            // Arrange
            var engine = new FSMEngine(new FakeNodeFactory());

            // Act
            engine.CreateNode("FakeType", "A");

            // Assert
            engine.GetNode("A").Should().NotBeNull();
        }

        [Fact]
        public void CreateNode_ThenGetNodeNames_HaveCount()
        {
            // Arrange
            var engine = new FSMEngine(new FakeNodeFactory());

            // Act
            engine.CreateNode("FakeType", "A");
            engine.CreateNode("FakeType", "B");

            // Assert
            engine.GetNodeNames().Should()
                //.Contain("A", "B");
                .HaveCount(2);
        }

    }
}
