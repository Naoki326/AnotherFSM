using System.Collections;
using StateMachine;
namespace Orders;

public static class Container
{
    [FSMNode("IgnoredConstructorKey", Key = "BusinessQuery")]
    public sealed class LookupWorker : EnumFSMNode
    {
        public LookupWorker() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL");
        protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL");
    }
}
[FSMNode("Conflict")] public sealed class FirstWorker : EnumFSMNode
{ protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL"); }
[FSMNode("CONFLICT")] public sealed class SecondWorker : EnumFSMNode
{ protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL"); }
[FSMNode("Generic")] public sealed class GenericWorker<T> : EnumFSMNode
{ protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL"); }
[FSMNode(null!)] public sealed class InvalidKeyWorker : EnumFSMNode
{ protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL"); }
public static class IdentityUse { public static Type Node => typeof(Container.LookupWorker); }
