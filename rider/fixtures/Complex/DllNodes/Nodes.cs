using FixtureSafety;
using StateMachine;
namespace Issue34.BinaryLibrary;
[FSMNode("HostDllKey")]
public sealed class BinaryWorker : ControlledNode { }
public static class BinaryEntry
{
    public static void Run() => Execution.Forbidden<object>("BinaryEntry.Run");
}
