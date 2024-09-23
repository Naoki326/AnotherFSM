using Masa.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace StateMachine
{

    public partial class StateMachineBoard
    {

        [Parameter]
        public string? Style { get; set; }

        [Parameter]
        public string? Class { get; set; }

        [Parameter]
        public List<string> NodeTypeNames { get; set; } = default!;

        public event Action<string>? ScriptChanged;

        protected override void OnInitialized()
        {
            if (NodeTypeNames is not null && NodeTypeNames.Count > 0)
            {
                var colors = s_colors.GetEnumerator();
                foreach (var nodeType in NodeTypeNames)
                {
                    colors.MoveNext();
                    NodeTypes.Add(nodeType, colors.Current);
                }
            }
        }

        [Parameter] public EventCallback DataInitializer { get; set; }

        private Dictionary<string, string> NodeTypes = [];
        private static IEnumerable<string> s_colors
        {
            get
            {
                while (true)
                {
                    yield return "red";
                    yield return "pink";
                    yield return "purple";
                    yield return "deep-purple";
                    yield return "indigo";
                    yield return "blue";
                    yield return "light-blue";
                    yield return "cyan";
                    yield return "teal";
                    yield return "green";
                    yield return "light-green";
                    yield return "lime";
                    yield return "yellow";
                    yield return "amber";
                    yield return "orange";
                    yield return "deep-orange";
                    yield return "brown";
                    yield return "blue-grey";
                    yield return "grey";
                }
            }
        }

        private MStateMachineFlow _drawflow = default!;

        async Task InnerNodeSelected(string id)
        {
            var node = await _drawflow.GetNodeFromIdAsync<NodeData>(id);
            if (node != null)
                await OnNodeSelected.InvokeAsync(node.Data);
        }

        async Task InnerNodeUnselected(string id)
        {
            await OnNodeUnselected.InvokeAsync();
        }

        [Parameter] public EventCallback<NodeData> OnNodeSelected { get; set; }

        [Parameter] public EventCallback OnNodeUnselected { get; set; }

        [Parameter] public EventCallback<ConnectionData> OnConnectionSelected { get; set; }

        [Parameter] public EventCallback OnConnectionUnselected { get; set; }

        [Parameter] public FSMEngine StateMachineEngine { get; set; } = default!;

        private FSMEngine engine = default!;
        protected override async Task OnParametersSetAsync()
        {
            if (StateMachineEngine != engine)
            {
                await ClearAsync();
                engine = StateMachineEngine;
                ScriptChanged?.Invoke(engine.ToString());
                await CreateFromEngine();
            }
            await base.OnParametersSetAsync();
        }

    }
    public partial class StateMachineBoard
    {
        public string Export()
        {
            string script = "";
            foreach (var state in engine)
            {
                script += state.ToString();
            }
            return script;
        }

        public async Task ClearAsync()
        {
            if (_drawflow == null)
            {
                return;
            }
            if (engine == null)
                return;
            engine.ClearNodes();
            ScriptChanged?.Invoke(engine.ToString());
            await _drawflow.ClearAsync();
        }

        private async Task CreateFromEngine()
        {
            if (_drawflow is null)
                return;
            foreach (string stateName in engine.GetNodeNames())
            {
                var state = engine[stateName];
                await _drawflow.AddNodeAsync(
                    id: int.Parse(state.FlowID),
                    name: state.Name,
                    inputs: 1,
                    outputs: 1,
                    clientX: state.PosX,
                    clientY: state.PosY,
                    offsetX: 0,
                    offsetY: 0,
                    className: state.Color,
                    data: new NodeData
                    {
                        Type = state.ClassType,
                        Color = state.Color,
                        Name = state.Name,
                        Message = $"message from {state.ClassType}!",
                    },
                    html: $"<div df-data style=\"text-align: center;cursor: pointer; text-overflow: ellipsis; white-space: nowrap; overflow: hidden;\">{state.Name}<br>({state.ClassType})</div>");
            }

            foreach (string stateName in engine.GetNodeNames())
            {
                var state = engine[stateName];
                foreach (var transition in state.GetFSMTransitions())
                {
                    if (await _drawflow.GetNodesFromNameAsync(transition.Target.Name) is List<int> nodeInputs
                        && await _drawflow.GetNodesFromNameAsync(transition.Source.Name) is List<int> nodeOutputs)
                    {
                        var id0 = nodeOutputs[0].ToString();
                        var id1 = nodeInputs[0].ToString();
                        await _drawflow.AddConnectionAsync(id0, id1, "output_1", "input_1", transition.Trigger.EventName);
                    }
                }
            }
        }

        [Parameter]
        public StateMachineFlowEditorMode Mode { get; set; } = StateMachineFlowEditorMode.Edit;

        private bool isImport;
        public async Task ImportAsync(string _importData)
        {
            if (isImport)
                return;
            if (string.IsNullOrWhiteSpace(_importData))
            {
                return;
            }
            isImport = true;
            await ClearAsync();
            try
            {
                engine.CreateStateMachine(_importData);
            }
            catch (Exception e)
            {
                await PopupService.EnqueueSnackbarAsync($"Import failed! Exception: {e.Message}", AlertTypes.Error);
            }
            try
            {
                ScriptChanged?.Invoke(engine.ToString());
                await CreateFromEngine();
            }
            catch (Exception e)
            {
                await PopupService.EnqueueSnackbarAsync($"Import failed! Exception: {e.Message}", AlertTypes.Error);
            }
            finally
            {
                isImport = false;
            }
        }

        // $"<div df-data style=\"text-align: center;cursor: pointer;\">{nodeInput.Name}<br>({nodeInput.Data.Type})</div>";
        public async Task UpdateNodeHTMLAsync(string nodeName, string html)
        {
            if (!engine.ContainsNode(nodeName))
                return;
            string nodeId = engine[nodeName].FlowID;
            if (await _drawflow.GetNodeFromIdAsync<NodeData>(nodeId) is StateMachineFlowNode<NodeData> nodeInput
                && nodeInput.Data != null && nodeInput.Class != null && nodeInput.Name != null)
            {
                //nodeInput.Data.Name = nodeInput.Name;
                //nodeInput.Name = nodeInput.Name;
                nodeInput.Html = html;
                //await _drawflow.UpdateNodeDataAsync(nodeId, nodeInput.Data, nodeInput.Name);
                await _drawflow.UpdateNodeHTMLAsync(nodeId, nodeInput.Html);
            }
        }

    }

    //创建、删除节点和连线
    public partial class StateMachineBoard
    {

        private bool changeNodeNameDialog = false;

        private string inputName = "";

        private string tempNodeId = "";
        private async Task NodeDblClick(string id)
        {
            tempNodeId = id;
            var nodeInput = await _drawflow.GetNodeFromIdAsync<NodeData>(tempNodeId);
            inputName = nodeInput.Name;
            changeNodeNameDialog = true;
        }

        private async Task OnNodeNameKeyUp(KeyboardEventArgs args)
        {
            if (args.Key == "Enter")
                await ChangeNodeName();
        }
        private async Task ChangeNodeName()
        {
            if (string.IsNullOrEmpty(inputName))
            {
                await PopupService.EnqueueSnackbarAsync($"InputName is null", AlertTypes.Error);
                return;
            }
            if (engine.GetNodeNames().Any(p => p == inputName))
            {
                await PopupService.EnqueueSnackbarAsync($"Node {inputName} Exist", AlertTypes.Error);
                return;
            }
            if (await _drawflow.GetNodeFromIdAsync<NodeData>(tempNodeId) is StateMachineFlowNode<NodeData> nodeInput
                && nodeInput.Data != null && nodeInput.Class != null && nodeInput.Name != null)
            {
                engine.TryChangeNodeName(nodeInput.Name, inputName);
                ScriptChanged?.Invoke(engine.ToString());

                nodeInput.Data.Name = inputName;
                nodeInput.Name = inputName;
                nodeInput.Html = $"<div df-data style=\"text-align: center;cursor: pointer; text-overflow: ellipsis; white-space: nowrap; overflow: hidden;\">{inputName}<br>({nodeInput.Data.Type})</div>";
                await _drawflow.UpdateNodeDataAsync(tempNodeId, nodeInput.Data, inputName);
                await _drawflow.UpdateNodeHTMLAsync(tempNodeId, nodeInput.Html);
            }
            changeNodeNameDialog = false;
            inputName = "";
        }

        private void CancelChangeNodeName()
        {
            inputName = "";
            changeNodeNameDialog = false;
        }

        public async Task ChangeNodeNameAsync(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(newName))
                throw new InvalidOperationException("Node name is null");
            if (!engine.ContainsNode(oldName))
                throw new KeyNotFoundException($"Node {oldName} doesn't exist!");
            if (engine.ContainsNode(newName))
                throw new InvalidOperationException($"Node {inputName} Exist");

            if (await _drawflow.GetNodeFromIdAsync<NodeData>(engine[oldName].FlowID) is StateMachineFlowNode<NodeData> nodeInput
                && nodeInput.Id != null && nodeInput.Data != null && nodeInput.Class != null && nodeInput.Name != null)
            {
                engine.TryChangeNodeName(oldName, newName);
                ScriptChanged?.Invoke(engine.ToString());

                nodeInput.Data.Name = newName;
                nodeInput.Name = newName;
                nodeInput.Html = $"<div df-data style=\"text-align: center;cursor: pointer; text-overflow: ellipsis; white-space: nowrap; overflow: hidden;\">{newName}<br>({nodeInput.Data.Type})</div>";
                await _drawflow.UpdateNodeDataAsync(nodeInput.Id, nodeInput.Data, newName);
                await _drawflow.UpdateNodeHTMLAsync(nodeInput.Id, nodeInput.Html);
            }
        }

        ExDragEventArgs tempArgs = default!;

        private async Task DropAsync(ExDragEventArgs args)
        {
            var nodeType = args.DataTransfer.Data.Value;
            if (string.IsNullOrEmpty(nodeType))
            { return; }

            string nodeNameSuffix = "";
            int suffixI = 1;

            //原先是有同名Node就退出，这里改成自动改名称
            //await PopupService.EnqueueSnackbarAsync($"Node {nodeType} create failed! Exception: {nodeType} already exist", AlertTypes.Error);
            //return;
            while (engine.TryGetNode(nodeType + nodeNameSuffix, out _))
            {
                nodeNameSuffix = suffixI.ToString();
                suffixI++;
            }

            await _drawflow.DragNodeAsync(
                name: nodeType + nodeNameSuffix,
                inputs: 1,
                outputs: 1,
                clientX: args.ClientX,
                clientY: args.ClientY,
                offsetX: args.DataTransfer.Data.OffsetX,
                offsetY: args.DataTransfer.Data.OffsetY,
                className: NodeTypes[nodeType],
                data: new NodeData
                {
                    Type = nodeType,
                    Color = NodeTypes[nodeType],
                    Name = nodeType + nodeNameSuffix,
                    Message = $"message from {nodeType}!",
                },
                html: $"<div df-data style=\"text-align: center;cursor: pointer; text-overflow: ellipsis; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; overflow: hidden;\">{nodeType + nodeNameSuffix}<br>({nodeType})</div>");

            tempArgs = args;

        }

        public async Task ZoomAsync(double zoom)
        {
            if (_drawflow is null)
                return;
            await _drawflow.ZoomAsync(zoom);
        }

        private async Task NodeCreated(string nodeId)
        {
            if (isImport)
                return;
            try
            {
                if (await _drawflow.GetNodeFromIdAsync<NodeData>(nodeId) is StateMachineFlowNode<NodeData> node
                    && node.Data != null
                    && node.Class != null && node.Name != null)
                {
                    engine.CreateNode(node.Data.Type, node.Name);
                    var state = engine[node.Name];
                    state.PosX = Math.Round(node.Pos_X);
                    state.PosY = Math.Round(node.Pos_Y);
                    state.Color = node.Data.Color;
                    state.ClassType = node.Data.Type;
                    state.FlowID = nodeId;
                    //初始化节点内部发出的事件
                    if (state.GetType().GetCustomAttributes(typeof(FSMNodeAttribute), true).FirstOrDefault() is FSMNodeAttribute info
                        && info.Indexes.Length == info.EventDescriptions.Length)
                    {
                        state.EventDescriptions = Enumerable.Range(0, info.Indexes.Length)
                            .Select(i => new NodeEventDescription() { Index = info.Indexes[i], Description = info.EventDescriptions[i] })
                            .ToList();
                        foreach (var ed in state.EventDescriptions)
                        {
                            if (!engine.TryGetEvent(ed.Description, out FSMEvent e))
                            { e = new FSMEvent(ed.Description); engine.AddEvent(e); }
                            state.SetBranchEvent(ed.Index, e);
                        }
                    }
                    await OnNodeCreated.InvokeAsync(node.Name);
                    ScriptChanged?.Invoke(engine.ToString());
                }
            }
            catch (Exception ex)
            {
                await PopupService.EnqueueSnackbarAsync($"Node {nodeId} create failed! Exception: {ex.Message}", AlertTypes.Error);
                await _drawflow.RemoveNodeAsync(nodeId);
            }
        }

        [Parameter]
        public EventCallback<string> OnNodeCreated { get; set; }

        public async Task RemoveNode(string nodeName)
        {
            if (!engine.ContainsNode(nodeName))
                throw new KeyNotFoundException($"Node {nodeName} not found!");
            var nodeId = engine[nodeName].FlowID;
            await _drawflow.RemoveNodeAsync(nodeId);
        }

        private void NodeRemoved(string nodeId)
        {
            if (engine.FirstOrDefault(p => engine[p.Name].FlowID == nodeId) is IFSMNode nd)
            {
                engine[nd.Name].ClearTransition();
                engine.TryDeleteNode(nd.Name);
                ScriptChanged?.Invoke(engine.ToString());
            }
        }

        public void OpenChangeNodeNameDialog(string nodeName)
        {
            if (!engine.ContainsNode(nodeName))
                return;
            tempNodeId = engine[nodeName].FlowID;
            inputName = nodeName;
            changeNodeNameDialog = true;
        }

        public void OpenChangeConnectionNameDialog(string outputNodeName, string inputNodeName)
        {
            tempConnectArgs = new FlowConnectionArgs()
            {
                InputClass = "input_1",
                OutputClass = "output_1",
                InputId = engine[inputNodeName].FlowID,
                OutputId = engine[outputNodeName].FlowID,
            };
            inputName = engine[outputNodeName].GetFSMTransition(inputNodeName).Trigger.EventName;
            changeConnectionNameDialog = true;
        }

        private bool changeConnectionNameDialog = false;

        private FlowConnectionArgs tempConnectArgs = default!;
        private async Task OnCDKeyUp(KeyboardEventArgs args)
        {
            if (args.Key == "Enter")
                await ChangeConnectionName();
        }

        private async Task ChangeConnectionName()
        {
            if (string.IsNullOrEmpty(inputName))
            {
                await PopupService.EnqueueSnackbarAsync($"Input is empty!", AlertTypes.Error);
                return;
            }

            try
            {
                if (await _drawflow.GetNodeFromIdAsync<NodeData>(tempConnectArgs.InputId) is StateMachineFlowNode<NodeData> nodeInput
                && nodeInput.Class != null && nodeInput.Name != null
                && await _drawflow.GetNodeFromIdAsync<NodeData>(tempConnectArgs.OutputId) is StateMachineFlowNode<NodeData> nodeOutput
                && nodeOutput.Class != null && nodeOutput.Name != null)
                {
                    await ChangeConnectionNameAsync(nodeOutput.Name, nodeInput.Name, inputName);
                }
            }
            catch (Exception ex)
            {
                await PopupService.EnqueueSnackbarAsync(ex.Message, AlertTypes.Error);
            }
            //await _drawflow.SetConnectionNameAsync(tempConnectArgs.OutputId, tempConnectArgs.InputId, tempConnectArgs.OutputClass, tempConnectArgs.InputClass, inputName);
            //if (await _drawflow.GetNodeFromIdAsync<NodeData>(tempConnectArgs.InputId) is StateMachineFlowNode<NodeData> nodeInput
            //    && nodeInput.Class != null && nodeInput.Name != null
            //    && await _drawflow.GetNodeFromIdAsync<NodeData>(tempConnectArgs.OutputId) is StateMachineFlowNode<NodeData> nodeOutput
            //    && nodeOutput.Class != null && nodeOutput.Name != null)
            //{
            //    if (!engine.TryGetEvent(inputName, out FSMEvent? sEvent))
            //    {
            //        sEvent = new FSMEvent(inputName);
            //    }
            //    if (!engine[nodeOutput.Name].HasTransition(sEvent))
            //    {
            //        engine[nodeOutput.Name].DeleteTransition(engine[nodeInput.Name]);
            //        engine[nodeOutput.Name].AddTransition(sEvent, engine[nodeInput.Name]);
            //        ScriptChanged?.Invoke(engine.ToString());
            //    }
            //    else
            //    {
            //        await PopupService.EnqueueSnackbarAsync($"Connection {inputName} create failed! Exception: Current node Contains {inputName}", AlertTypes.Error);
            //    }
            //}
            inputName = "";
            changeConnectionNameDialog = false;
        }

        public async Task ChangeConnectionNameAsync(string fromNodeName, string toNodeName, string newName)
        {
            if (string.IsNullOrEmpty(fromNodeName) || string.IsNullOrEmpty(toNodeName))
                throw new InvalidOperationException("Node name is null");
            if (string.IsNullOrEmpty(newName))
                throw new InvalidOperationException("Connection name is null");
            if (!engine.ContainsNode(fromNodeName))
                throw new KeyNotFoundException($"Node {fromNodeName} doesn't exist!");
            if (!engine.ContainsNode(toNodeName))
                throw new InvalidOperationException($"Node {toNodeName} doesn't Exist");

            await _drawflow.SetConnectionNameAsync(engine[fromNodeName].FlowID, engine[toNodeName].FlowID, "output_1", "input_1", newName);

            if (!engine.TryGetEvent(newName, out FSMEvent? sEvent))
            {
                sEvent = new FSMEvent(newName);
                engine.AddEvent(sEvent);
            }
            if (!engine[fromNodeName].HasTransition(sEvent))
            {
                engine[fromNodeName].DeleteTransition(engine[toNodeName]);
                engine[fromNodeName].AddTransition(sEvent, engine[toNodeName]);
                ScriptChanged?.Invoke(engine.ToString());
            }
            else
            {
                throw new InvalidOperationException($"Connection {newName} create failed! Exception: Current node Contains {newName}");
            }
        }

        private void CancelChangeConnectionName()
        {
            inputName = "";
            changeConnectionNameDialog = false;
        }

        private async Task ConnectionCreated(FlowConnectionArgs args)
        {
            if (isImport)
                return;
            if (await _drawflow.GetNodeFromIdAsync<NodeData>(args.InputId) is StateMachineFlowNode<NodeData> nodeInput
                && nodeInput.Class != null && nodeInput.Name != null
                && await _drawflow.GetNodeFromIdAsync<NodeData>(args.OutputId) is StateMachineFlowNode<NodeData> nodeOutput
                && nodeOutput.Class != null && nodeOutput.Name != null)
            {
                string eventNameSuffix = "";
                int suffixI = 1;
                string defaultEventName = "NextEvent";
                if (engine.TryGetEvent(defaultEventName, out FSMEvent? sEvent))
                {
                    //流程引擎中已有该事件

                    while (engine[nodeOutput.Name].HasTransition(sEvent))
                    {
                        //当前output节点已有该事件
                        eventNameSuffix = suffixI.ToString();
                        suffixI++;
                        if (!engine.TryGetEvent(defaultEventName + eventNameSuffix, out sEvent))
                        {
                            sEvent = new FSMEvent(defaultEventName + eventNameSuffix);
                            engine.AddEvent(sEvent);
                        }
                    }
                }
                else
                {
                    sEvent = new FSMEvent(defaultEventName);
                    engine.AddEvent(sEvent);
                }

                if (!engine[nodeOutput.Name].HasTransition(engine[nodeInput.Name]))
                {
                    await _drawflow.SetConnectionNameAsync(args.OutputId, args.InputId, args.OutputClass, args.InputClass, defaultEventName + eventNameSuffix);
                    engine[nodeOutput.Name].AddTransition(sEvent, engine[nodeInput.Name]);
                    ScriptChanged?.Invoke(engine.ToString());
                }
                else
                {
                    await _drawflow.RemoveSingleConnectionAsync(args.OutputId, args.InputId, args.OutputClass, args.InputClass);
                    await PopupService.EnqueueSnackbarAsync($"Connection {defaultEventName + eventNameSuffix} create failed! Exception: From {nodeOutput.Name} to {nodeInput.Name} exist.", AlertTypes.Error);
                }
            }
        }

        public async Task RemoveConnectionAsync(string fromNodeName, string toNodeName)
        {
            if (!engine.ContainsNode(fromNodeName)
                || !engine.ContainsNode(toNodeName))
            {
                throw new KeyNotFoundException("Connection not found!");
            }
            await _drawflow.RemoveSingleConnectionAsync(engine[fromNodeName].FlowID, engine[toNodeName].FlowID, "output_1", "input_1");
        }
        private async void ConnectionSelected(FlowConnectionArgs args)
        {
            if (engine.FirstOrDefault(p => p.FlowID == args.OutputId) is IFSMNode fromNode
                && engine.FirstOrDefault(p => p.FlowID == args.InputId) is IFSMNode toNode)
            {
                await OnConnectionSelected.InvokeAsync(new ConnectionData()
                {
                    FromNodeName = fromNode.Name,
                    ToNodeName = toNode.Name,
                });
            }
        }

        private async Task ConnectionDblClick(FlowConnectionArgs args)
        {
            if (isImport)
                return;
            tempConnectArgs = args;
            if (await _drawflow.GetNodeFromIdAsync<NodeData>(args.InputId) is StateMachineFlowNode<NodeData> nodeInput
                && nodeInput.Class != null && nodeInput.Name != null
                && await _drawflow.GetNodeFromIdAsync<NodeData>(args.OutputId) is StateMachineFlowNode<NodeData> nodeOutput
                && nodeOutput.Class != null && nodeOutput.Name != null)
            {
                inputName = engine[nodeOutput.Name].GetFSMTransition(nodeInput.Name).Trigger.EventName;
            }
            changeConnectionNameDialog = true;
        }

        private void ConnectionRemoved(FlowConnectionArgs args)
        {
            if (engine.FirstOrDefault(p => p.FlowID == args.InputId) is IFSMNode Input
                && engine.FirstOrDefault(p => p.FlowID == args.OutputId) is IFSMNode Output)
            {
                Output.DeleteTransition(Input);
                ScriptChanged?.Invoke(engine.ToString());
            }
        }

        private async Task ConnectionCancel(FlowConnectionError error)
        {
            await PopupService.EnqueueSnackbarAsync($"Connect failed! Exception: {error.ErrorMessage}", AlertTypes.Error);
        }

        private async Task NodeMoved(string nodeId)
        {
            if (await _drawflow.GetNodeFromIdAsync<NodeData>(nodeId) is StateMachineFlowNode<NodeData> node
                && node.Data != null && node.Name != null)
            {
                engine[node.Name].PosX = node.Pos_X;
                engine[node.Name].PosY = node.Pos_Y;
                ScriptChanged?.Invoke(engine.ToString());
            }
        }

    }

}
