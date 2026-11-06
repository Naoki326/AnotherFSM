using System.Collections;
using StateMachine;
namespace Inventory;

[FSMNode("PayloadTransform")]
public sealed class ChangeWorker : EnumFSMNode
{
    public ChangeWorker() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL");
    protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL");
}
public static class IdentityUse { public static Type Node => typeof(ChangeWorker); }
