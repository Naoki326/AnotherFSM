using StateMachine;

var bindings = new FSMContextBindings<FlowInput>();
bindings.Default<IView>(x => x.Init);
bindings.Node<IView>("Work.Read", x => x.Private);
bindings.DefaultSource(x => x.Readonly);
#if BAD_BINDING
bindings.Node<IView>("Work.Read", x => x.Incompatible);
#endif
var input = new FlowInput { Init = new Business() };
#if BAD_INIT
input.Init = new Business();
#endif
#if BAD_PRIVATE
input.Private = new Business();
#endif
#if BAD_READONLY
input.Readonly = new Business();
#endif
#if BAD_INPUT
FSMContextFlow<FlowInput> flow = null!;
flow.Prepare(new WrongInput());
#endif
#if BAD_CONTEXT_REBIND
var node = new ReadNode();
node.Context = new Business();
#endif
#if BAD_BIND_ENTRY
((IFSMContextNode)new ReadNode()).BindContext(new Business());
#endif
#if OLD_CTOR
_ = new FSMExecutor((IFSMNode)null!, new FSMEvent("Done"));
#endif
#if OLD_CTOR_ASYNC
_ = new FSMExecutor((IFSMNode)null!, new FSMEvent("Done"), true);
#endif
#if OLD_CTOR_ENDS
_ = new FSMExecutor((IFSMNode)null!, new[] { new FSMEvent("Done") });
#endif
#if OLD_CTOR_ENDS_ASYNC
_ = new FSMExecutor((IFSMNode)null!, new[] { new FSMEvent("Done") }, true);
#endif
#if OLD_RESTART
await ((FSMExecutor)null!).RestartAsync();
#endif
#if OLD_RESTART_DATA
await ((FSMExecutor)null!).RestartAsync(new Business());
#endif
#if OLD_RESTART_NODE
await ((FSMExecutor)null!).RestartAsync((IFSMNode)null!);
#endif
#if OLD_RESTART_NODE_DATA
await ((FSMExecutor)null!).RestartAsync((IFSMNode)null!, new Business());
#endif
#if OLD_CONTEXT
_ = ((IFSMNode)null!).Context;
#endif
#if OLD_WRAPPER
_ = new FSMNodeContext();
#endif
#if OLD_GENERIC_WRAPPER
_ = new FSMNodeContext<Business>();
#endif
#if OLD_HANDLE
((FSMExecutor)null!).Handle(new FSMEvent("Done"));
#endif
var engine = new FSMEngine(new PositiveFactory());
engine.CreateNode("Read", "Read");
engine.AttachEvent("Read", "Done", 1);
using (var prepared = engine.BindContext<FlowInput>(b => b.Default<IView>(x => x.Init)).Prepare(input))
using (var executor = await prepared.StartAsync("Read", "Done"))
    await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
var plain = new FSMEngine(new PositiveFactory());
plain.CreateNode("Plain", "Start");
plain.AttachEvent("Start", "Done", 1);
using (var prepared = plain.Prepare())
using (var executor = await prepared.StartAsync("Start", "Done"))
    await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine("Typed source selections, explicit aggregate execution and dependency-free preparation compile and run.");

interface IView { string? Value { get; } }
sealed class Business : IView { public string? Value => null; }
sealed class FlowInput
{
    public Business Init { get; init; } = new();
    public Business Private { get; private set; } = new();
    public Business Readonly { get; } = new();
    public object Incompatible { get; } = new();
}
sealed class WrongInput { }
sealed class PlainNode : BaseFSMNode
{
    protected override Task ExecuteAsync() { Control.CheckPause(); PublishEvent(1); return Task.CompletedTask; }
}
sealed class PositiveFactory : IFSMNodeFactory
{
    public IFSMNode CreateNode(string key) => key == "Read" ? new ReadNode() : new PlainNode();
    public Type GetNodeType(string key) => key == "Read" ? typeof(ReadNode) : typeof(PlainNode);
    public string GetNodeFeatureName(Type type) => type.Name;
}
sealed class ReadNode : BaseFSMNode<IView>
{
    protected override Task ExecuteAsync()
    {
        _ = Context.Value;
        _ = Control.Token;
        _ = TriggerEvent;
        PublishEvent(1);
        return Task.CompletedTask;
    }
}
