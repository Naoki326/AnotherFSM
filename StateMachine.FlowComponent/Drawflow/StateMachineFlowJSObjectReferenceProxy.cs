using Masa.Blazor.JSInterop;
using Microsoft.JSInterop;

namespace StateMachine;

public class StateMachineFlowJSObjectReferenceProxy : JSObjectReferenceProxy, IStateMachineFlowJSObjectReferenceProxy
{
    public StateMachineFlowJSObjectReferenceProxy(IJSObjectReference jsObjectReference) : base(jsObjectReference)
    {
    }

    public async Task SetMode(StateMachineFlowEditorMode mode)
    {
        await InvokeVoidAsync("setMode", mode.ToString().ToLower());
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
        return await InvokeAsync<string>("addNode",
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
        return await InvokeAsync<string>("addNodeById",
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
        return await InvokeAsync<string>("dragNode",
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
        await InvokeVoidAsync("zoom", zoom);
    }

    //addConnection(id_output, id_input, output_class, input_class)
    public async Task AddConnectionAsync(string id_output, string id_input, string output_class, string input_class, string eventName)
    {
        await InvokeVoidAsync("addConnection", id_output, id_input, output_class, input_class, eventName);
    }

    //removeSingleConnection
    public async Task RemoveSingleConnectionAsync(string id_output, string id_input, string output_class, string input_class)
    {
        await InvokeVoidAsync("removeSingleConnection", id_output, id_input, output_class, input_class);
    }
    public async Task SetConnectionNameAsync(string id_output, string id_input, string output_class, string input_class, string eventName)
    {
        await InvokeVoidAsync("setConnectionName", id_output, id_input, output_class, input_class, eventName);
    }
    public async Task<StateMachineFlowNode<TData>?> GetNodeFromIdAsync<TData>(string nodeId)
    {
        return await InvokeAsync<StateMachineFlowNode<TData>>("getNodeFromId", nodeId);
    }
    public async Task<List<int>?> GetNodesFromNameAsync(string nodeName)
    {
        return await InvokeAsync<List<int>>("getNodesFromName", nodeName);
    }

    public async Task RemoveNodeAsync(string nodeId)
    {
        await InvokeVoidAsync("removeNodeId", $"node-{nodeId}");
    }

    public async Task UpdateNodeDataAsync(string nodeId, object data, string name)
    {
        await InvokeVoidAsync("updateNodeDataFromId", nodeId, data, name);
    }

    public async Task ClearAsync()
    {
        await InvokeVoidAsync("clear");
    }

    public async Task<string?> ExportAsync(bool indented = false)
    {
        return await InvokeAsync<string?>("export", indented);
    }

    public async Task ImportAsync(string json)
    {
        await InvokeVoidAsync("import", json);
    }

    public async Task AddInputAsync(string nodeId)
    {
        await InvokeVoidAsync("addNodeInput", nodeId);
    }

    public async Task AddOutputAsync(string nodeId)
    {
        await InvokeVoidAsync("addNodeOutput", nodeId);
    }

    public async Task RemoveInputAsync(string nodeId, string inputClass)
    {
        await InvokeVoidAsync("removeNodeInput", nodeId, inputClass);
    }

    public async Task RemoveOutputAsync(string nodeId, string outputClass)
    {
        await InvokeVoidAsync("removeNodeOutput", nodeId, outputClass);
    }

    public async Task UpdateNodeHTMLAsync(string nodeId, string html)
    {
        await InvokeVoidAsync("updateNodeHtml", nodeId, html);
    }

    public async Task FocusNodeAsync(string nodeId)
    {
        await InvokeVoidAsync("focusNode", nodeId);
    }

    public async Task CenterNodeAsync(string nodeId, bool animate)
    {
        await InvokeVoidAsync("centerNode", nodeId, animate);
    }

    public async Task UpdateConnectionNodesAsync(string nodeId)
    {
        await InvokeVoidAsync("updateConnectionNodes", nodeId);
    }

    public async Task RemoveConnectionNodeIdAsync(string nodeId)
    {
        await InvokeVoidAsync("removeConnectionNodeId", nodeId);
    }
}

public enum StateMachineFlowEditorMode
{
    Edit,

    Fixed,

    View
}
