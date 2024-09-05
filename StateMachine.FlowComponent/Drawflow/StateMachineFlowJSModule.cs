using Masa.Blazor.JSInterop;
using Microsoft.JSInterop;

namespace StateMachine;

public class StateMachineFlowJSModule : JSModule
{
    public StateMachineFlowJSModule(IJSRuntime js) : base(js, "./_content/StateMachine.FlowComponent/drawflow-export.js")
    {
    }

    public async ValueTask<IStateMachineFlowJSObjectReferenceProxy> Init(string selector, DotNetObjectReference<object> _dotNetObjectReference,
        StateMachineFlowEditorMode mode)
    {
        var jsObject = await InvokeAsync<IJSObjectReference>("init", selector, _dotNetObjectReference, mode.ToString().ToLower());
        return new StateMachineFlowJSObjectReferenceProxy(jsObject);
    }
}
