using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using StateMachine;

namespace FSMDemo.Runtime;

/// <summary>The transport-neutral JSON boundary used by the React browser adapter.</summary>
public sealed class RuntimeSession : IAsyncDisposable
{
    private readonly EngineManager _manager;
    private readonly Action<string> _publish;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _publishSync = new();
    private volatile bool _disposed;

    public RuntimeSession(IFSMNodeFactory factory, Action<string> publish, bool browser = true)
    {
        _manager = new EngineManager(NullLogger<EngineManager>.Instance, factory, browser);
        _publish = publish;
        _manager.StateChanged += (state, node) => Publish(new RuntimeEvent("StateChanged", state.ToString(), node));
        _manager.NodeActivated += node => Publish(new RuntimeEvent("NodeActivated", NodeName: node));
        _manager.NodeDeactivated += node => Publish(new RuntimeEvent("NodeDeactivated", NodeName: node));
    }

    private void Publish(RuntimeEvent notification)
    {
        lock (_publishSync)
        {
            if (_disposed) return;
            // A disconnected JS subscriber must not fault the executor's consumer.
            try { _publish(JsonSerializer.Serialize(notification, RuntimeJsonContext.Default.RuntimeEvent)); }
            catch { /* The transport owns reporting callback failures. */ }
        }
    }

    public async Task<string> InvokeAsync(string operation, string payloadJson)
    {
        var entered = false;
        try
        {
            // Serialize graph operations across async import/start boundaries. Controls
            // must bypass this gate so they can cancel a pending start or pause.
            if (operation is not ("pauseExecution" or "continueExecution" or "stopExecution" or "getExecutionStatus"))
            {
                await _operations.WaitAsync();
                entered = true;
            }
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var document = JsonDocument.Parse(payloadJson);
            var p = document.RootElement;
            if (p.ValueKind != JsonValueKind.Object) throw new ArgumentException("Runtime payload must be a JSON object");
            if (operation == "createNode" && p.TryGetProperty("groupDefs", out _))
                throw new NotSupportedException(RemovedComposition.Diagnostic);
            object? value = operation switch
            {
                "createNode" => _manager.CreateNode(p.GetProperty("type").GetString()!, p.GetProperty("name").GetString()!,
                    p.GetProperty("posX").GetDouble(), p.GetProperty("posY").GetDouble(),
                    p.TryGetProperty("color", out var color) ? color.GetString()! : ""),
                "getNode" => _manager.GetNode(Text(p, "name")) ?? throw new InvalidOperationException("Node not found"),
                "getNodes" => _manager.GetNodes(),
                "getStatus" => _manager.GetStatus(),
                "getExecutionStatus" => _manager.GetExecutionStatus(),
                "startExecution" => Edited(await _manager.StartExecution(Text(p, "startNodeName"), Text(p, "endEventName"))),
                "pauseExecution" => Edited(await _manager.PauseExecution()),
                "continueExecution" => Edited(_manager.ContinueExecution()),
                "stopExecution" => Edited(await _manager.StopExecution()),
                "importScript" => await ImportAsync(Text(p, "script")),
                "exportScript" => _manager.Export(),
                "getModuleDefinitions" => _manager.GetModuleDefinitions(),
                "getModuleInstances" => _manager.GetModuleInstances(),
                "getConnections" => _manager.GetConnections(),
                "createConnection" => Connected(_manager.CreateConnection(Text(p, "fromNodeName"), Text(p, "toNodeName"), Text(p, "eventName"))),
                "deleteConnection" => Edited(_manager.DeleteConnection(Text(p, "fromNodeName"), Text(p, "toNodeName"), OptionalText(p, "eventName"))),
                "renameConnection" => Edited(_manager.RenameConnection(Text(p, "fromNodeName"), Text(p, "toNodeName"), Text(p, "newEventName"), OptionalText(p, "eventName"))),
                "createModuleInstance" => _manager.AddModuleInstance(Text(p, "moduleName"), Text(p, "instanceName"), p.GetProperty("posX").GetDouble(), p.GetProperty("posY").GetDouble()),
                "deleteModuleInstance" => Edited(_manager.DeleteModuleInstance(Text(p, "instanceName"))),
                "updateModuleInstancePosition" => Edited(_manager.UpdateModuleInstancePosition(Text(p, "instanceName"), p.GetProperty("posX").GetDouble(), p.GetProperty("posY").GetDouble())),
                "deleteNode" => Edited(_manager.DeleteNode(Text(p, "name"))),
                "renameNode" => Edited(_manager.RenameNode(Text(p, "name"), Text(p, "newName"))),
                "updateNodePosition" => Edited(_manager.UpdateNodePosition(Text(p, "name"), p.GetProperty("posX").GetDouble(), p.GetProperty("posY").GetDouble())),
                "updateNodeEvent" => Edited(_manager.UpdateNodeEvent(Text(p, "name"), p.GetProperty("index").GetInt32(), Text(p, "newEventName"))),
                "updateGroupDefs" => throw new NotSupportedException(RemovedComposition.Diagnostic),
                _ => throw new InvalidOperationException($"Unknown runtime operation '{operation}'"),
            };
            return JsonSerializer.Serialize(new RuntimeReply(value), RuntimeJsonContext.Default.RuntimeReply);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new RuntimeError(ex.Message), RuntimeJsonContext.Default.RuntimeError);
        }
        finally
        {
            if (entered) _operations.Release();
        }
    }

    private async Task<EngineStatus> ImportAsync(string script)
    {
        await _manager.ImportAsync(script);
        Publish(new RuntimeEvent("EngineImported"));
        return _manager.GetStatus();
    }

    private static string Text(JsonElement p, string name) => p.GetProperty(name).GetString()
        ?? throw new ArgumentException($"'{name}' must be a string");

    private static string? OptionalText(JsonElement p, string name) => p.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static object? Connected((bool Success, string Error) result)
        => result.Success ? null : throw new InvalidOperationException(result.Error);

    private static object? Edited(bool success)
        => success ? null : throw new InvalidOperationException("Edit or execution control failed: object not found or operation not allowed");

    public async ValueTask DisposeAsync()
    {
        lock (_publishSync) _disposed = true;
        // Cancel first; do not wait behind an operation that is retiring an executor.
        await _manager.StopExecution();
        await _operations.WaitAsync();
        try { await _manager.DisposeAsync(); }
        finally { _operations.Release(); }
    }
}

internal sealed record RuntimeReply(object? Value);
internal sealed record RuntimeError(string Error);
internal sealed record RuntimeEvent(string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? State = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CurrentNode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NodeName = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RuntimeReply))]
[JsonSerializable(typeof(RuntimeError))]
[JsonSerializable(typeof(RuntimeEvent))]
[JsonSerializable(typeof(ExecutionStatus))]
[JsonSerializable(typeof(NodeInfo))]
[JsonSerializable(typeof(List<NodeInfo>))]
[JsonSerializable(typeof(EngineStatus))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ModuleInstanceDto))]
[JsonSerializable(typeof(List<ModuleInstanceDto>))]
[JsonSerializable(typeof(List<ModuleDefDto>))]
[JsonSerializable(typeof(List<ConnectionDto>))]
internal partial class RuntimeJsonContext : JsonSerializerContext;
