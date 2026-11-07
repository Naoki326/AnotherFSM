// Static provenance witness only. No runtime attribute/getter is instantiated/invoked.
namespace StateMachine;
[System.AttributeUsage(System.AttributeTargets.Class)]
public sealed class FSMNodeSourceAttribute : System.Attribute
{
    public FSMNodeSourceAttribute(string sourceCSPath, int[] events) => FixtureSafety.Execution.Forbidden<object>("OwnedSourceAttribute.ctor");
    public string SourceCSPath => FixtureSafety.Execution.Forbidden<string>("OwnedSourceAttribute.SourceCSPath.get");
}
