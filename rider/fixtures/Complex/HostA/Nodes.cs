extern alias LeftAssembly;
extern alias RightAssembly;
using FixtureSafety;
using Registration = StateMachine.FSMNodeAttribute;
namespace Issue34.HostA;

[Registration("CurrentHostKey")]
public sealed class InvoiceWorker : ControlledNode
{
    public InvoiceWorker() => Execution.Forbidden<object>("InvoiceWorker.ctor");
    public override void Execute() => Execution.Forbidden<object>("InvoiceWorker.Execute");
}

// Compile-time identity witnesses only. typeof is inside a guarded getter, never invoked by preparation.
public static class AliasWitness
{
    public static Type[] Types
    {
        get
        {
            Execution.Forbidden<object>("AliasWitness.Types.get");
            return new[] {
                typeof(LeftAssembly::Issue34.SameNames.CollisionWorker),
                typeof(RightAssembly::Issue34.SameNames.CollisionWorker),
                typeof(Issue34.BinaryLibrary.BinaryWorker),
                typeof(Issue34.PackageLibrary.ParcelWorker),
                typeof(Issue34.ProjectLibrary.BufferWorker<string>),
                typeof(Issue34.ProjectLibrary.Warehouse.UnregisteredShelfWorker),
                typeof(Issue34.ProjectLibrary.GenericWarehouse<InvoiceWorker>.SlotWorker) };
        }
    }
}
