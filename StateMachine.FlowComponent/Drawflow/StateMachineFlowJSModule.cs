using Microsoft.JSInterop;
using StateMachine;

namespace StateMachine.FlowComponent;

public class StateMachineFlowJSModule
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public StateMachineFlowJSModule(IJSRuntime js)
    {
        _js = js;
    }

    public async ValueTask<IStateMachineFlowJSObjectReferenceProxy> Init(string selector, DotNetObjectReference<object> _dotNetObjectReference,
        StateMachineFlowEditorMode mode)
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./_content/Naoki.AnotherFSM.FlowComponent/drawflow-export.js");
        var jsObject = await _module.InvokeAsync<IJSObjectReference>("init", selector, _dotNetObjectReference, mode.ToString().ToLower());
        return new StateMachineFlowJSObjectReferenceProxy(jsObject);
    }
}
