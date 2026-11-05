using StateMachine;

#if GROUP
_ = new GroupNode();
#elif GROUP_ONE
_ = new GroupNode<object>();
#elif GROUP_TWO
_ = new GroupNode<object, object>();
#elif PARALLEL
_ = new ParallelNode();
#elif PARALLEL_ONE
_ = new ParallelNode<object>();
#elif PARALLEL_TWO
_ = new ParallelNode<object, object>();
#elif HOOK
new FSMEngine(null).GroupNodeStateChanged += (_, _) => { };
#elif DESCRIPTOR
_ = new FSMDescribe();
#elif BASE_GROUP
_ = typeof(BaseGroupNode);
#else
// A non-composition EngineBackendNode consumer still compiles and is wired.
var engine = new FSMEngine(new OrdinaryFactory());
engine.CreateNode("Ordinary", "N");
engine.ReinitGroupNode();
if (!object.ReferenceEquals(((OrdinaryBackend)engine.GetNode("N")).Backend, engine))
    throw new System.Exception("Ordinary engine injection was lost");
#endif

sealed class OrdinaryBackend : EngineBackendNode
{
    protected override async System.Collections.Generic.IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
    {
        yield return Yield.None;
        await System.Threading.Tasks.Task.CompletedTask;
    }
}

sealed class OrdinaryFactory : IFSMNodeFactory
{
    public IFSMNode CreateNode(string name) => new OrdinaryBackend();
    public System.Type GetNodeType(string name) => typeof(OrdinaryBackend);
    public string GetNodeFeatureName(System.Type type) => "Ordinary";
}
