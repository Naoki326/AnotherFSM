using FixtureSafety;
using StateMachine;
namespace Issue34.PackageLibrary;
[FSMNode("LocalPackageKey")]
public sealed class ParcelWorker : ControlledNode { }
public static class PackageEntry
{
    public static void Run() => Execution.Forbidden<object>("PackageEntry.Run");
}
