using FixtureSafety;
using StateMachine;

namespace Issue34.MatrixDependency;

[FSMNode("MatrixProjectKey")]
public sealed class ProjectWorker : ControlledNode
{
    public ProjectWorker() => Execution.Forbidden<object>("MatrixDependency.ProjectWorker.ctor");
    public override void Execute() => Execution.Forbidden<object>("MatrixDependency.ProjectWorker.Execute");
}
