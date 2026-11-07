// Deliberately a different source version; NOT the source used to compile the DLL.
namespace Issue34.SourceCases;
[StateMachine.FSMNode("SourceMismatchKey")]
[StateMachine.FSMNodeSource(OwnedCandidate.Path, new int[0])]
public sealed class MismatchWorker : FixtureSafety.ControlledNode
{
    public int Version => FixtureSafety.Execution.Forbidden<int>("MismatchWorker.Version.v2");
}
