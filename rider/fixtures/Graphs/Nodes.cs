using System.Collections;
using StateMachine;
namespace Graphs;

// Node name Entry, factory key GraphKey and C# type GraphWorker deliberately differ.
[FSMNode("GraphKey", [1, 2, 3, 4, 5], ["Done", "Retry", "Fault", "Stop", "Abort"])]
public sealed class GraphWorker : EnumFSMNode
{
    public GraphWorker() => GraphExecutionSentinel.Forbidden<object>("GraphWorker.ctor");
    [StateMachine.Interface.FSMProperty("Never evaluate business getter", false, 0)]
    public string BusinessValue => GraphExecutionSentinel.Forbidden<string>("GraphWorker.BusinessValue.get");
    public static string RuntimeKey => GraphExecutionSentinel.Forbidden<string>("GraphWorker.RuntimeKey.get");
    protected override IEnumerable ExecuteEnumerable() => GraphExecutionSentinel.Forbidden<IEnumerable>("GraphWorker.ExecuteEnumerable");
}

// No explicit branch metadata. A keyword must not jump to the similarly named C# enum.
[FSMNode("GraphBareKey")]
public sealed class BareWorker : EnumFSMNode
{
    public BareWorker() => GraphExecutionSentinel.Forbidden<object>("BareWorker.ctor");
    protected override IEnumerable ExecuteEnumerable() => GraphExecutionSentinel.Forbidden<IEnumerable>("BareWorker.ExecuteEnumerable");
}
public enum MisleadingBranch { Error = -1, Break = 3, Cancel = 4 }
public static class IdentityUse
{
    public static Type Node
    {
        get
        {
            GraphExecutionSentinel.Forbidden<object>("IdentityUse.Node.get");
            return typeof(GraphWorker);
        }
    }
}
