using FixtureSafety;
using StateMachine;
#if HOST_A
namespace Issue34.HostA.Shared;
[FSMNode("SharedHostKey")]
public sealed class LinkedInvoiceWorker : ControlledNode { }
#elif HOST_B
namespace Issue34.HostB.Shared;
[FSMNode("SharedHostKey")]
public sealed class SharedDispatchWorker : ControlledNode { }
#else
#error SharedBinding.cs must be compiled only by HostA or HostB.
#endif
