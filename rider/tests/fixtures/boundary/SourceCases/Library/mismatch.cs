namespace Issue34.SourceCases;
[StateMachine.FSMNode("SourceMismatchKey")]
[StateMachine.FSMNodeSource(OwnedCandidate.Path, new int[0])]
public sealed class MismatchWorker : FixtureSafety.ControlledNode
{
    // Compiled version 1. Never evaluate this getter.
    public int Version => FixtureSafety.Execution.Forbidden<int>("MismatchWorker.Version.v1");
}
