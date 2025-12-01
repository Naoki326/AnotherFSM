using DemoNodes;
using Masa.Blazor;
using Microsoft.AspNetCore.Components;
using StateMachine;
using System.Reflection;

namespace StateMachineDemoShared.Pages;

public partial class ProcedureView : IDisposable
{
    private StateMachineBoard smBoard = default!;
    private List<string> nodeTypes = default!;
    public required FSMEngine Engine { get; set; }

    [Inject]
    public NodeTypes NodeTypes { get; set; } = default!;

    [Inject]
    public IFSMNodeFactory NodeFactory { get; set; } = default!;

    protected override Task OnInitializedAsync()
    {
        Engine = FSMEngineBuilder.Create()
            .ConfigureNodeFactory(NodeFactory)
            .ConfigureFSMDefine(build =>
                {
                    //build
                    //.AddNode<SleepNode>("Sleep")
                    //.AddConnection("NextEvent", "Start", "Sleep");
                }
            )
            .Build();
        nodeTypes = [..NodeTypes.GetNodeTypes()
                    .Where(p => p.GetCustomAttributes<FSMNodeAttribute>().Any())
                    .OrderBy(p => p.GetCustomAttributes<FSMNodeAttribute>().First().Id)
                    .Select(p => p.GetCustomAttributes<FSMNodeAttribute>().First())
                    .Select(p => p.Key)
            ];
        return Task.CompletedTask;
    }


    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            smBoard.ScriptChanged += SmBoard_ScriptChanged;
        }
        return Task.CompletedTask;
    }

    private void SmBoard_ScriptChanged(string script)
    {
        if (!isImporting)
        {
            InvokeAsync(StateHasChanged);
        }
    }

    private bool isImporting = false;

    void NodeCreated(string nodeName)
    {
        //IFSMNode state = Engine[nodeName];
    }

    [Parameter]
    public EventCallback AfterInit { get; set; }

    private async void InitDraw()
    {
        await smBoard.ZoomAsync(0.8);
        //await ImportFromScript();
        ////这里不打开两次，drawflow会有奇怪的bug
        //await ImportFromScript();
        await AfterInit.InvokeAsync();
    }

    public async Task ImportFromScript(string importScript)
    {
        if (string.IsNullOrEmpty(importScript))
        {
            await smBoard.ClearAsync();
        }
        else
        {
            try
            {
                isImporting = true;
                await smBoard.ImportAsync(importScript);
            }
            finally
            {
                isImporting = false;
            }
        }
        await InvokeAsync(StateHasChanged);
    }

    private void ResetEventDescriptions()
    {
        if (State?.GetType().GetCustomAttributes(typeof(FSMNodeAttribute), true).FirstOrDefault() as FSMNodeAttribute is FSMNodeAttribute fsmNodeInfo
            && fsmNodeInfo.Indexes.Length == fsmNodeInfo.EventDescriptions.Length)
        {
            State.EventDescriptions = Enumerable
                .Range(0, fsmNodeInfo.Indexes.Length)
                .Select(p => new NodeEventDescription()
                {
                    Index = fsmNodeInfo.Indexes[p],
                    Description = fsmNodeInfo.EventDescriptions[p]
                })
                .ToList();
            foreach (var ed in State.EventDescriptions)
            {
                if (!Engine.TryGetEvent(ed.Description, out FSMEvent e))
                {
                    e = new FSMEvent(ed.Description);
                    Engine.AddEvent(e);
                }
                State.SetBranchEvent(ed.Index, e);
            }
        }
    }

    private async Task DeleteNodeAsync()
    {
        try
        {
            await smBoard.RemoveNodeAsync(State.Name);
            State = null;
        }
        catch (Exception ex)
        {
            await PopupService.EnqueueSnackbarAsync(ex.Message, AlertTypes.Error);
        }
    }

    private async Task DeleteConnectionAsync()
    {
        try
        {
            await DeleteConnection(selectedConnection);
            selectedConnection = null;
        }
        catch (Exception ex)
        {
            await PopupService.EnqueueSnackbarAsync(ex.Message, AlertTypes.Error);
        }
    }

    private async Task ClearAsync()
    {
        await smBoard.ClearAsync();
    }

    bool _exportDialog = false;
    bool _importDialog = false;
    string _exportData = "";
    string _importData = "";
    private void Export()
    {
        _exportData = smBoard.Export();
        _exportDialog = true;
    }
    private void Import()
    {
        _importDialog = true;
    }
    private async Task ImportConfirm()
    {
        await smBoard.ImportAsync(_importData);
        _importDialog = false;
    }
    private void ImportCancel()
    {
        _importDialog = false;
    }

    private IFSMNode? State;

    private string selectedNodeName = "";
    private async Task NodeSelected(NodeData data)
    {
        selectedNodeName = data.Name;
        if (Engine.TryGetNode(data.Name, out IFSMNode? node))
        {
            State = node;
            await InvokeAsync(StateHasChanged);
            await SetActive(node);
        }
    }

    private async Task NodeUnselected()
    {
        State = null;
        await InvokeAsync(StateHasChanged);
        if (Engine.TryGetNode(selectedNodeName, out IFSMNode? node))
        {
            await SetInactive(node);
        }
    }
    private void ChangeNodeName()
    {
        smBoard.OpenChangeNodeNameDialog(selectedNodeName);
    }

    private void ChangeConnectionName()
    {
        smBoard.OpenChangeConnectionNameDialog(selectedConnection.FromNodeName, selectedConnection.ToNodeName);
    }

    ConnectionData? selectedConnection;
    private void ConnectionSelected(ConnectionData data)
    {
        selectedConnection = data;
    }

    private void ConnectionUnselected()
    {
        selectedConnection = null;
    }

    private async Task DeleteConnection(ConnectionData data)
    {
        await smBoard.RemoveConnectionAsync(data.FromNodeName, data.ToNodeName);
    }

    public async Task SetActive(string name)
    {
        await SetActive(Engine[name]);
    }

    public async Task SetActive(IFSMNode node)
    {
        await smBoard.UpdateNodeHTMLAsync(node.Name, $"<div df-data class=\"Procedure-select-block\" style=\"text-align: center;cursor: pointer; text-overflow: ellipsis; white-space: nowrap; overflow: hidden;\">{node.Name}<br>({node.ClassType})</div>");
    }

    public async Task SetInactive(string name)
    {
        await SetInactive(Engine[name]);
    }

    public async Task SetInactive(IFSMNode node)
    {
        await smBoard.UpdateNodeHTMLAsync(node.Name, $"<div df-data style=\"text-align: center;cursor: pointer; text-overflow: ellipsis; white-space: nowrap; overflow: hidden;\">{node.Name}<br>({node.ClassType})</div>");
    }

    public void Dispose()
    {
        smBoard.ScriptChanged -= SmBoard_ScriptChanged;
    }
}
