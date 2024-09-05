using BemIt;
using Masa.Blazor;
using Masa.Blazor.Attributes;
using Masa.Blazor.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace StateMachine;

public class MStateMachineFlow : MDrop, IAsyncDisposable
{
    [Inject] private StateMachineFlowJSModule DrawflowJSModule { get; set; } = null!;

    [Parameter] public StateMachineFlowEditorMode Mode { get; set; }

    [Parameter] public EventCallback DataInitializer { get; set; }


    [Parameter] public EventCallback<string> OnNodeCreated { get; set; }

    [Parameter] public EventCallback<string> OnNodeRemoved { get; set; }

    [Parameter] public EventCallback<string> OnNodeSelected { get; set; }

    [Parameter] public EventCallback<string> OnNodeUnselected { get; set; }

    [Parameter] public EventCallback<string> OnNodeMoved { get; set; }

    [Parameter] public EventCallback<string> OnNodeDataChanged { get; set; }

    [Parameter] public EventCallback OnImport { get; set; }

    [Parameter] public EventCallback<FlowConnectionArgs> OnConnectionCreated { get; set; }
    [Parameter] public EventCallback<FlowConnectionArgs> OnConnectionRemoved { get; set; }
    [Parameter] public EventCallback<FlowConnectionArgs> OnConnectionDblClick { get; set; }
    [Parameter] public EventCallback<string> OnNodeDblClick { get; set; }
    [Parameter] public EventCallback<FlowConnectionArgs> OnConnectionStart { get; set; }
    [Parameter] public EventCallback<FlowConnectionError> OnConnectionCancel { get; set; }
    [Parameter] public EventCallback<FlowConnectionArgs> OnConnectionSelected { get; set; }
    [Parameter] public EventCallback OnConnectionUnselected { get; set; }

    private StateMachineFlowEditorMode? _prevMode;
    private IStateMachineFlowJSObjectReferenceProxy? _drawflowProxy;
    private DotNetObjectReference<object>? _interopHandleReference;


    private readonly ModifierBuilder _modifierBuilder = new Block("parent-drawflow").CreateModifierBuilder();
    protected override string ClassString => _modifierBuilder.Add(Mode, "mode").AddClass(base.ClassString).Build();


    //protected override string ClassString => new Block("m-drawflow").Modifier(Mode, "mode").AddClass(base.ClassString).Build();

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (_prevMode.HasValue && _prevMode != Mode)
        {
            _prevMode = Mode;
            _drawflowProxy?.SetMode(Mode);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender)
        {
            _interopHandleReference = DotNetObjectReference.Create<object>(new StateMachineFlowInteropHandle(this));
            _drawflowProxy = await DrawflowJSModule.Init(ElementReference.GetSelector()!, _interopHandleReference, Mode);

            await DataInitializer.InvokeAsync();
        }
    }

    [MasaApiPublicMethod]
    public async Task<string?> AddNodeAsync(
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
        if (_drawflowProxy == null) return null;

        return await _drawflowProxy.AddNodeAsync(name, inputs, outputs, clientX, clientY, offsetX, offsetY, className, data, html)
                                   .ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task<string?> AddNodeAsync(
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
        if (_drawflowProxy == null) return null;

        return await _drawflowProxy.AddNodeAsync(id, name, inputs, outputs, clientX, clientY, offsetX, offsetY, className, data, html)
                                   .ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task<string?> DragNodeAsync(
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
        if (_drawflowProxy == null) return null;

        return await _drawflowProxy.DragNodeAsync(name, inputs, outputs, clientX, clientY, offsetX, offsetY, className, data, html)
                                   .ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task ZoomAsync(double zoom)
    {
        if (_drawflowProxy == null) return;
        await _drawflowProxy.ZoomAsync(zoom)
                                   .ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task RemoveNodeAsync(string nodeId)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.RemoveNodeAsync(nodeId).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task AddConnectionAsync(string id_output, string id_input, string output_class, string input_class, string eventName)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.AddConnectionAsync(id_output, id_input, output_class, input_class, eventName).ConfigureAwait(false);
    }


    [MasaApiPublicMethod]
    public async Task RemoveSingleConnectionAsync(string id_output, string id_input, string output_class, string input_class)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.RemoveSingleConnectionAsync(id_output, id_input, output_class, input_class).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task SetConnectionNameAsync(string id_output, string id_input, string output_class, string input_class, string eventName)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.SetConnectionNameAsync(id_output, id_input, output_class, input_class, eventName).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task<StateMachineFlowNode<TData>?> GetNodeFromIdAsync<TData>(string nodeId)
    {
        if (_drawflowProxy == null) return null;

        return await _drawflowProxy.GetNodeFromIdAsync<TData>(nodeId).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task<List<int>?> GetNodesFromNameAsync(string nodeName)
    {
        if (_drawflowProxy == null) return null;

        return await _drawflowProxy.GetNodesFromNameAsync(nodeName).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task UpdateNodeDataAsync(string nodeId, object data, string name)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.UpdateNodeDataAsync(nodeId, data, name).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task UpdateNodeHTMLAsync(string nodeId, string html)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.UpdateNodeHTMLAsync(nodeId, html).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task ClearAsync()
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.ClearAsync().ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task ImportAsync(string json)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.ImportAsync(json).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task<string?> ExportAsync(bool indented = false)
    {
        if (_drawflowProxy == null) return null;

        return await _drawflowProxy.ExportAsync(indented).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task AddInputAsync(string nodeId)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.AddInputAsync(nodeId).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task AddOutputAsync(string nodeId)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.AddOutputAsync(nodeId).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task RemoveInputAsync(string nodeId, string inputClass)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.RemoveInputAsync(nodeId, inputClass).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task RemoveOutputAsync(string nodeId, string outputClass)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.RemoveOutputAsync(nodeId, outputClass).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task FocusNodeAsync(string nodeId)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.FocusNodeAsync(nodeId).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task CenterNodeAsync(string nodeId, bool animate = true)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.CenterNodeAsync(nodeId, animate).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task UpdateConnectionNodesAsync(string nodeId)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.UpdateConnectionNodesAsync(nodeId).ConfigureAwait(false);
    }

    [MasaApiPublicMethod]
    public async Task RemoveConnectionNodeIdAsync(string nodeId)
    {
        if (_drawflowProxy == null) return;

        await _drawflowProxy.RemoveConnectionNodeIdAsync(nodeId).ConfigureAwait(false);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        try
        {
            _interopHandleReference?.Dispose();

            if (_drawflowProxy != null)
            {
                await _drawflowProxy.DisposeAsync();
            }
        }
        catch (Exception)
        {
            // ignored
        }
        GC.SuppressFinalize(this);
    }

    ~MStateMachineFlow()
    {
        ((IAsyncDisposable)this).DisposeAsync();
    }
}
