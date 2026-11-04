using StateMachine;

namespace StateMachineTest.TestInfrastructure;

/// <summary>
/// 一个总是返回 <see cref="FakeNode"/> 的节点工厂。
/// </summary>
/// <remarks>
/// 用于那些"需要 <c>FSMEngine.CreateNode</c> 真正造出一个节点"的测试。
/// 与 <see cref="ThrowingNodeFactory"/> 的区别：那个在被调用时抛异常（用于不需要造节点的测试），
/// 这个则老老实实返回一个 <see cref="FakeNode"/>。
/// </remarks>
public sealed class FakeNodeFactory : IFSMNodeFactory
{
    public IFSMNode CreateNode(string featureName) => new FakeNode();

    public Type GetNodeType(string featureName) => typeof(FakeNode);

    public string GetNodeFeatureName(Type type) => type.Name;
}
