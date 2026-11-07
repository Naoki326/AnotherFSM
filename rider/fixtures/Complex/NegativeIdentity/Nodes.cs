extern alias ImpostorOnly;
using FixtureSafety;
using RealTag = StateMachine.FSMNodeAttribute;
using FakeTag = ImpostorOnly::StateMachine.FSMNodeAttribute;
using FakeNode = ImpostorOnly::StateMachine.IFSMNode;
namespace Issue34.IdentityNegatives;

[RealTag("IdentityControlKey")]
public sealed class ValidWorker : ControlledNode { }
[FakeTag("ForeignAttributeKey")]
public sealed class WrongAttributeWorker : ControlledNode { }
[RealTag("ForeignInterfaceKey")]
public sealed class WrongInterfaceWorker : FakeNode
{
    public WrongInterfaceWorker() => Execution.Forbidden<object>("WrongInterfaceWorker.ctor");
    public void Execute() => Execution.Forbidden<object>("WrongInterfaceWorker.Execute");
}
[FakeTag("ForeignPairKey")]
public sealed class ForeignPairWorker : FakeNode
{
    public ForeignPairWorker() => Execution.Forbidden<object>("ForeignPairWorker.ctor");
    public void Execute() => Execution.Forbidden<object>("ForeignPairWorker.Execute");
}
