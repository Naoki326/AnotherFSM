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
Console.WriteLine("Typed source selections and initialization compile.");

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
sealed class ReadNode : BaseFSMNode<IView>
{
    protected override Task ExecuteAsync()
    {
        _ = Context.Value;
        _ = Control.Token;
        _ = TriggerEvent;
        return Task.CompletedTask;
    }
}
