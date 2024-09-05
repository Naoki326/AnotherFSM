using Microsoft.JSInterop;

namespace StateMachine;

public interface IStateMachineFlowJSObjectReferenceProxy : IJSObjectReference, IStateMachineFlow
{
    Task SetMode(StateMachineFlowEditorMode mode);
}

public interface IStateMachineFlow
{
    Task<string> AddNodeAsync
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
        string html);

    Task<string> AddNodeAsync
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
        string html);

    Task<string> DragNodeAsync
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
        string html);

    Task ZoomAsync(double zoom);

    Task<StateMachineFlowNode<TData>?> GetNodeFromIdAsync<TData>(string nodeId);

    Task<List<int>?> GetNodesFromNameAsync(string nodeName);

    Task RemoveNodeAsync(string nodeId);

    Task UpdateNodeDataAsync(string nodeId, object data, string name);

    Task UpdateNodeHTMLAsync(string nodeId, string html);

    Task ClearAsync();

    Task<string?> ExportAsync(bool indented = false);

    Task ImportAsync(string json);

    Task AddInputAsync(string nodeId);

    Task AddOutputAsync(string nodeId);
    Task AddConnectionAsync(string id_output, string id_input, string output_class, string input_class, string eventName);
    Task RemoveSingleConnectionAsync(string id_output, string id_input, string output_class, string input_class);
    Task SetConnectionNameAsync(string id_output, string id_input, string output_class, string input_class, string eventName);

    Task RemoveInputAsync(string nodeId, string inputClass);

    Task RemoveOutputAsync(string nodeId, string inputClass);

    Task FocusNodeAsync(string nodeId);

    Task CenterNodeAsync(string nodeId, bool animate);

    Task UpdateConnectionNodesAsync(string nodeId);

    Task RemoveConnectionNodeIdAsync(string nodeId);
}
