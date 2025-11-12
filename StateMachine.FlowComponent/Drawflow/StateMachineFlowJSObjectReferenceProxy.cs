using Microsoft.JSInterop;

namespace StateMachine;

public class StateMachineFlowJSObjectReferenceProxy : IStateMachineFlowJSObjectReferenceProxy
{
    private readonly IJSObjectReference _js;

    public StateMachineFlowJSObjectReferenceProxy(IJSObjectReference jsObjectReference)
    {
        _js = jsObjectReference;
    }

    public async Task SetMode(StateMachineFlowEditorMode mode)
    {
        await _js.InvokeVoidAsync("setMode", mode.ToString().ToLower());
    }

    public async Task<string> AddNodeAsync
    (
        string name,
        int inputs,
        int outputs,
        double clientX,
        double clientY,
        double offsetX,
        double offsetY,
        string? className,
        object? data,
        string html)
    {
        return await _js.InvokeAsync<string>("addNode",
            name,
            inputs,
            outputs,
            clientX,
            clientY,
            offsetX,
            offsetY,
            className,
            data ?? new { },
            html);
    }


    public async Task<string> AddNodeAsync
    (
        int id,
        string name,
        int inputs,
        int outputs,
        double clientX,
        double clientY,
        double offsetX,
        double offsetY,
        string? className,
        object? data,
        string html)
    {
        return await _js.InvokeAsync<string>("addNodeById",
            id,
            name,
            inputs,
            outputs,
            clientX,
            clientY,
            offsetX,
            offsetY,
            className,
            data ?? new { },
            html);
    }

    public async Task<string> DragNodeAsync
    (
        string name,
        int inputs,
        int outputs,
        double clientX,
        double clientY,
        double offsetX,
        double offsetY,
        string? className,
        object? data,
        string html)
    {
        return await _js.InvokeAsync<string>("dragNode",
            name,
            inputs,
            outputs,
            clientX,
            clientY,
            offsetX,
            offsetY,
            className,
            data ?? new { },
            html);
    }

    public async Task ZoomAsync(double zoom)
    {
        await _js.InvokeVoidAsync("zoom", zoom);
    }

    public async Task AddConnectionAsync(string id_output, string id_input, string output_class, string input_class, string eventName)
    {
        await _js.InvokeVoidAsync("addConnection", id_output, id_input, output_class, input_class, eventName);
    }

    public async Task RemoveSingleConnectionAsync(string id_output, string id_input, string output_class, string input_class)
    {
        await _js.InvokeVoidAsync("removeSingleConnection", id_output, id_input, output_class, input_class);
    }
    public async Task SetConnectionNameAsync(string id_output, string id_input, string output_class, string input_class, string eventName)
    {
        await _js.InvokeVoidAsync("setConnectionName", id_output, id_input, output_class, input_class, eventName);
    }
    public async Task<StateMachineFlowNode<TData>?> GetNodeFromIdAsync<TData>(string nodeId)
    {
        return await _js.InvokeAsync<StateMachineFlowNode<TData>>("getNodeFromId", nodeId);
    }
    public async Task<List<int>?> GetNodesFromNameAsync(string nodeName)
    {
        return await _js.InvokeAsync<List<int>>("getNodesFromName", nodeName);
    }

    public async Task RemoveNodeAsync(string nodeId)
    {
        await _js.InvokeVoidAsync("removeNodeId", $"node-{nodeId}");
    }

    public async Task UpdateNodeDataAsync(string nodeId, object data, string name)
    {
        await _js.InvokeVoidAsync("updateNodeDataFromId", nodeId, data, name);
    }

    public async Task ClearAsync()
    {
        await _js.InvokeVoidAsync("clear");
    }

    public async Task<string?> ExportAsync(bool indented = false)
    {
        return await _js.InvokeAsync<string?>("export", indented);
    }

    public async Task ImportAsync(string json)
    {
        await _js.InvokeVoidAsync("import", json);
    }

    public async Task AddInputAsync(string nodeId)
    {
        await _js.InvokeVoidAsync("addNodeInput", nodeId);
    }

    public async Task AddOutputAsync(string nodeId)
    {
        await _js.InvokeVoidAsync("addNodeOutput", nodeId);
    }

    public async Task RemoveInputAsync(string nodeId, string inputClass)
    {
        await _js.InvokeVoidAsync("removeNodeInput", nodeId, inputClass);
    }

    public async Task RemoveOutputAsync(string nodeId, string outputClass)
    {
        await _js.InvokeVoidAsync("removeNodeOutput", nodeId, outputClass);
    }

    public async Task UpdateNodeHTMLAsync(string nodeId, string html)
    {
        await _js.InvokeVoidAsync("updateNodeHtml", nodeId, html);
    }

    public async Task FocusNodeAsync(string nodeId)
    {
        await _js.InvokeVoidAsync("focusNode", nodeId);
    }

    public async Task CenterNodeAsync(string nodeId, bool animate)
    {
        await _js.InvokeVoidAsync("centerNode", nodeId, animate);
    }

    public async Task UpdateConnectionNodesAsync(string nodeId)
    {
        await _js.InvokeVoidAsync("updateConnectionNodes", nodeId);
    }

    public async Task RemoveConnectionNodeIdAsync(string nodeId)
    {
        await _js.InvokeVoidAsync("removeConnectionNodeId", nodeId);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        return _js.InvokeAsync<TValue>(identifier, args);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        return _js.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }

    public ValueTask DisposeAsync()
    {
        return _js.DisposeAsync();
    }
}

public enum StateMachineFlowEditorMode
{
    Edit,

    Fixed,

    View
}
