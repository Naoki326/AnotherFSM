using System.Collections;
using StateMachine;

namespace Orders.Negatives
{
    [FSMNode("NotNode")] public sealed class PlainClass { }
    [FSMNode("ForbiddenFallback", Key = null!)] public sealed class InvalidNamedKey : EnumFSMNode
    { protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL"); }
    public class Outer<T>
    {
        [FSMNode("OuterGeneric")] public sealed class Inner : EnumFSMNode
        { protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL"); }
    }
}
namespace Shadow
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class FSMNodeAttribute(string key) : Attribute { public string Key { get; } = key; }
    [FSMNode("Impostor")] public sealed class FakeAttributeNode : EnumFSMNode
    { protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL"); }
}
