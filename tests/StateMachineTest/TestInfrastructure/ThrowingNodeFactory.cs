using StateMachine;

namespace StateMachineTest.TestInfrastructure;

/// <summary>
/// 测试用的节点工厂桩（stub）。
/// </summary>
/// <remarks>
/// 构造 <see cref="FSMEngine"/> 时必须传入一个 <see cref="IFSMNodeFactory"/>，
/// 但很多测试（例如"检查一个空的 engine"）根本不会去创建节点。
/// 这个桩在"没人调用 CreateNode"时毫无副作用；可一旦真的被调用，
/// 它就立刻抛 <see cref="NotSupportedException"/> —— 这正是测试想要的行为：
/// 它会把"你以为不会创建节点、其实创建了"这件事大声暴露出来，而不是悄悄出错。
/// 后续需要真实节点的测试，会换用更合适的工厂（例如基于反射的实现）。
/// </remarks>
public sealed class ThrowingNodeFactory : IFSMNodeFactory
{
    public IFSMNode CreateNode(string featureName)
        => throw new NotSupportedException(
            $"ThrowingNodeFactory 不会创建节点 '{featureName}'。" +
            "若测试确实需要真实节点，请改用 ReflectionNodeFactory 或自定义工厂。");

    public Type GetNodeType(string featureName)
        => throw new NotSupportedException();

    public string GetNodeFeatureName(Type type)
        => throw new NotSupportedException();
}
