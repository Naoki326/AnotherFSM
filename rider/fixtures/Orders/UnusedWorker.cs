using System.Collections;
using StateMachine;
namespace Orders;

[FSMNode("NeverUsed")]
public sealed class UnusedWorker : EnumFSMNode
{
    protected override IEnumerable ExecuteEnumerable() => throw new InvalidOperationException("BUSINESS_EXECUTION_SENTINEL");
}
public static class UnusedTypeReferences
{
    public static Type First => typeof(UnusedWorker);
    public static Type Second => typeof(UnusedWorker);
}
