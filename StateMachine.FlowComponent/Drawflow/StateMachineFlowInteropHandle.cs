using Microsoft.JSInterop;

namespace StateMachine;

public class StateMachineFlowInteropHandle
{
    private readonly MStateMachineFlow _drawflow;

    public StateMachineFlowInteropHandle(MStateMachineFlow drawflow)
    {
        _drawflow = drawflow;
    }

    [JSInvokable]
    public async Task OnNodeCreated(string nodeId)
    {
        await _drawflow.OnNodeCreated.InvokeAsync(nodeId);
    }

    [JSInvokable]
    public async Task OnNodeRemoved(string nodeId)
    {
        await _drawflow.OnNodeRemoved.InvokeAsync(nodeId);
    }

    [JSInvokable]
    public async Task OnNodeSelected(string nodeId)
    {
        await _drawflow.OnNodeSelected.InvokeAsync(nodeId);
    }

    [JSInvokable]
    public async Task OnNodeUnselected(string nodeId)
    {
        await _drawflow.OnNodeUnselected.InvokeAsync(nodeId);
    }

    [JSInvokable]
    public async Task OnConnectionSelected(string output_id, string input_id, string output_class, string input_class)
    {
        await _drawflow.OnConnectionSelected.InvokeAsync(new FlowConnectionArgs()
        {
            OutputId = output_id,
            InputId = input_id,
            OutputClass = output_class,
            InputClass = input_class,
        });
    }

    [JSInvokable]
    public async Task OnConnectionUnselected()
    {
        await _drawflow.OnConnectionUnselected.InvokeAsync();
    }

    [JSInvokable]
    public async Task OnNodeMoved(string nodeId)
    {
        await _drawflow.OnNodeMoved.InvokeAsync(nodeId);
    }

    [JSInvokable]
    public async Task OnNodeDataChanged(string nodeId)
    {
        await _drawflow.OnNodeDataChanged.InvokeAsync(nodeId);
    }

    [JSInvokable]
    public async Task OnImport()
    {
        await _drawflow.OnImport.InvokeAsync();
    }

    [JSInvokable]
    public async Task OnConnectionCreated(string output_id, string input_id, string output_class, string input_class)
    {
        await _drawflow.OnConnectionCreated.InvokeAsync(new FlowConnectionArgs()
        {
            OutputId = output_id,
            InputId = input_id,
            OutputClass = output_class,
            InputClass = input_class,
        });
    }

    [JSInvokable]
    public async Task OnConnectionRemoved(string output_id, string input_id, string output_class, string input_class)
    {
        await _drawflow.OnConnectionRemoved.InvokeAsync(new FlowConnectionArgs()
        {
            OutputId = output_id,
            InputId = input_id,
            OutputClass = output_class,
            InputClass = input_class,
        });
    }

    [JSInvokable]
    public async Task OnConnectionDblClick(string output_id, string input_id, string output_class, string input_class)
    {
        await _drawflow.OnConnectionDblClick.InvokeAsync(new FlowConnectionArgs()
        {
            OutputId = output_id,
            InputId = input_id,
            OutputClass = output_class,
            InputClass = input_class,
        });
    }

    [JSInvokable]
    public async Task OnNodeDblClick(string id)
    {
        await _drawflow.OnNodeDblClick.InvokeAsync(id);
    }

    [JSInvokable]
    public async Task OnConnectionStart(string output_id, string output_class)
    {
        await _drawflow.OnConnectionStart.InvokeAsync(new FlowConnectionArgs()
        {
            OutputId = output_id,
            OutputClass = output_class,
        });
    }

    [JSInvokable]
    public async Task OnConnectionCancel(int errorId, string errorMessage)
    {
        await _drawflow.OnConnectionCancel.InvokeAsync(new FlowConnectionError()
        {
            ErrorId = errorId,
            ErrorMessage = errorMessage,
        });
    }
}
