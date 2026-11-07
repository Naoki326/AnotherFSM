using FixtureSafety;
using StateMachine;
#if HOST_A
namespace Issue34.HostA.Shared;
[FSMNode("SharedHostKey")]
public sealed class LinkedInvoiceWorker : ControlledNode { }
public static class SharedWitness
{
    public static System.Type Type
    {
        get { Execution.Forbidden<object>("HostA.SharedWitness.Type.get"); return typeof(LinkedInvoiceWorker); }
    }
}
#elif HOST_B
namespace Issue34.HostB.Shared;
[FSMNode("SharedHostKey")]
public sealed class SharedDispatchWorker : ControlledNode { }
public static class SharedWitness
{
    public static System.Type Type
    {
        get { Execution.Forbidden<object>("HostB.SharedWitness.Type.get"); return typeof(SharedDispatchWorker); }
    }
}
#else
#error SharedBinding.cs must be compiled only by HostA or HostB.
#endif
