using System.Collections;
using StateMachine;

namespace DemoNodes;

public interface IQueryBusiness { int Value { get; set; } }
public interface IProcessBusiness { int Value { get; set; } }
public interface IApprovalBusiness { bool Approved { get; } }
public sealed class DemoJob : IQueryBusiness, IProcessBusiness
{
    public int Value { get; set; }
}
public sealed class DemoBusinessInput
{
    public DemoJob Job { get; } = new();
    // This example intentionally has no approval service. An Approval node fails
    // preparation, rather than silently running without a business dependency.
    public IApprovalBusiness? Approval { get; }
    public DemoBusinessInput(IApprovalBusiness? approval = null) => Approval = approval;
}

[FSMNode("BusinessQuery", "Populate job", [1], ["Next"], Id = 21)]
public partial class BusinessQueryNode : EnumFSMNode<IQueryBusiness>
{
    protected override IEnumerable ExecuteEnumerable()
    {
        Control.CheckPause();
        Context.Value = 41;
        yield return Yield.Event(1);
    }
}

[FSMNode("BusinessProcess", "Process shared job", [1], ["Done"], Id = 22)]
public partial class BusinessProcessNode : EnumFSMNode<IProcessBusiness>
{
    protected override IEnumerable ExecuteEnumerable()
    {
        Control.CheckPause();
        Context.Value++;
        yield return Yield.Event(1);
    }
}

[FSMNode("BusinessApproval", "Require approval service", [1], ["Approved"], Id = 23)]
public partial class BusinessApprovalNode : EnumFSMNode<IApprovalBusiness>
{
    protected override IEnumerable ExecuteEnumerable()
    {
        if (Context.Approved) yield return Yield.Event(1);
    }
}
