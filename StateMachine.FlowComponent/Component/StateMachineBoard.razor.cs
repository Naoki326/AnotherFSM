using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Diagnostics;
using System.Linq;

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

        [Inject]
        public IJSRuntime JSRuntime { get; set; } = default!;

        protected override void OnInitialized()
        {
            var colors = s_colors.GetEnumerator();
            if (NodeTypeNames is not null && NodeTypeNames.Count > 0)
            {
                foreach (var nodeType in NodeTypeNames)
                {
                    colors.MoveNext();
                    nodeTypes.Add(nodeType, colors.Current);
                }
            }
            // 若未提供 NodeTypeNames，则不再从运行时注册表回退，避免依赖全局扫描
        }

        [Parameter] public EventCallback DataInitializer { get; set; }

        private Dictionary<string, string> nodeTypes = [];
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
            {
                selectedNodeName = node.Data.Name;
            }
        }

        async Task InnerNodeUnselected(string id)
        {
            selectedNodeName = "";
        }

        [Parameter] public FSMEngine StateMachineEngine { get; set; } = default!;

        private FSMEngine engine = default!;

        private string selectedNodeName = "";
        private string selectedNodeFilePath = "";
        private bool nodeInfoDialog = false;
        protected override async Task OnParametersSetAsync()
        {
            if (StateMachineEngine != engine)
            {
                await ClearAsync();
                engine = StateMachineEngine;
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
            await _drawflow.ClearAsync();
        }

        private Dictionary<string, int> flowID2Index = [];
        private HashSet<int> usedIndex = [];
        private int GetNextIndex()
        {
            Random rd = new Random();
            var rdInt = rd.Next();
            while (usedIndex.Contains(rdInt))
            {
                rdInt = rd.Next(500, 1000);
            }
            usedIndex.Add(rdInt);
            return rdInt;
        }
        private int GetFlowIndex(string flowId)
        {
            if(int.TryParse(flowId, out int retInt))
                return retInt;
            if (!flowID2Index.ContainsKey(flowId))
            {
                flowID2Index[flowId] = GetNextIndex();
            }
            return flowID2Index[flowId];
        }

        private string GetFlowIndexStr(string flowId)
        {
            return GetFlowIndex(flowId).ToString();
        }

        [Parameter] public List<string> IgnoreTransNodes { get; set; }

        private async Task CreateFromEngine()
        {
            if (_drawflow is null)
                return;
            foreach (string stateName in engine.GetNodeNames())
            {
                var state = engine[stateName];
                await _drawflow.AddNodeAsync(
                    id: GetFlowIndex(state.FlowID),
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
                if(IgnoreTransNodes is not null && IgnoreTransNodes.Contains(stateName))
                {
                    continue;
                }
                var state = engine[stateName];
                foreach (var transition in state.GetFSMTransitions())
                {
                    if (IgnoreTransNodes is not null && IgnoreTransNodes.Contains(transition.Target.Name))
                    {
                        continue;
                    }
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
        
        private async Task ShowNodeInfo()
        {
            if (!string.IsNullOrEmpty(selectedNodeName) && engine != null)
            {
                if (!engine.TryGetNode(selectedNodeName, out IFSMNode? node))
                {
                    return;
                }
                if (node is IFSMNodeSourceInfo nodeWithPath)
                {
                    var mappedPath = nodeWithPath.SourceCSPath;
                    selectedNodeFilePath = mappedPath!;
                }
                else
                {
                    var nodeType = node.GetType();
                    var className = nodeType.Name;
                    var baseTypeName = className.Contains('`') ? className.Split('`')[0] : className;
                    // 尝试在解决方案目录内搜索源文件
                    var solutionRoot = GetSolutionRootPath();
                    var searchResult = SearchForSourceFile(solutionRoot, baseTypeName, nodeType.Namespace ?? "");
                    if (string.IsNullOrEmpty(searchResult))
                    {
                        return;
                        return;
                    }
                    selectedNodeFilePath = searchResult;
                }

                nodeInfoDialog = true;
                await InvokeAsync(StateHasChanged);
            }
        }
        
        private string GetSolutionRootPath()
        {
            try
            {
                // 从当前执行目录开始向上查找解决方案文件
                var currentDir = Directory.GetCurrentDirectory();
                var dir = new DirectoryInfo(currentDir);
                
                while (dir != null)
                {
                    if (Directory.GetFiles(dir.FullName, "*.sln").Length > 0)
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
                
                // 如果没找到.sln文件，尝试查找.git目录作为项目根目录
                dir = new DirectoryInfo(currentDir);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
                
                return currentDir; // 回退到当前目录
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }
        
        private string SearchForSourceFile(string rootPath, string typeName, string namespaceName)
        {
            try
            {
                // 在解决方案中搜索匹配的.cs文件
                var searchPatterns = new[]
                {
                    $"{typeName}.cs",
                    $"*{typeName}*.cs"
                };
                
                var allFiles = new List<string>();
                
                foreach (var pattern in searchPatterns)
                {
                    try
                    {
                        var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories)
                            .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("\\.git\\"))
                            .ToList();
                        allFiles.AddRange(files);
                    }
                    catch
                    {
                        // 忽略搜索过程中的错误
                    }
                }
                
                if (allFiles.Count > 0)
                {
                    // 优先返回完全匹配的文件
                    var exactMatches = allFiles.Where(f => Path.GetFileName(f).Equals($"{typeName}.cs", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (exactMatches.Count > 0)
                    {
                        // 如果提供了命名空间，进一步验证文件内容
                        foreach (var file in exactMatches)
                        {
                            try
                            {
                                var content = File.ReadAllText(file);
                                bool nsOk = string.IsNullOrWhiteSpace(namespaceName) || content.Contains($"namespace {namespaceName}");
                                bool classOk = content.Contains($"class {typeName}") || content.Contains($"partial class {typeName}");
                                if (nsOk && classOk)
                                    return file;
                            }
                            catch { }
                        }
                        // 如果验证失败，返回第一个完全匹配
                        return exactMatches[0];
                    }

                    // 如果没有完全匹配，尝试按命名空间和类名内容验证第一个匹配
                    foreach (var file in allFiles)
                    {
                        try
                        {
                            var content = File.ReadAllText(file);
                            bool nsOk = string.IsNullOrWhiteSpace(namespaceName) || content.Contains($"namespace {namespaceName}");
                            bool classOk = content.Contains($"class {typeName}") || content.Contains($"partial class {typeName}");
                            if (nsOk && classOk)
                                return file;
                        }
                        catch { }
                    }

                    // 最后回退到第一个匹配的文件
                    return allFiles[0];
                }
                
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        
        private void CloseNodeInfoDialog()
        {
            nodeInfoDialog = false;
        }
        
        private async Task CopyFilePath()
        {
            if (!string.IsNullOrEmpty(selectedNodeFilePath))
            {
                try
                {
                    await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", selectedNodeFilePath);
                }
                catch
                {
                    
                }
            }
        }

        private async Task OpenInVSCode()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selectedNodeFilePath))
                {
                    return;
                }

                // Normalize Windows path to vscode://file URI (use forward slashes)
                var normalized = selectedNodeFilePath.Replace('\\', '/');
                var uri = $"vscode://file/{normalized}";

                // Attempt to open via custom protocol in the current window
                await JSRuntime.InvokeVoidAsync("open", uri, "_self");
            }
            catch
            {
                // Fallback: copy path to clipboard so user can manually open
                try
                {
                    await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", selectedNodeFilePath);
                }
                catch { }
                
            }
        }

        private Task OpenInRiderLocal(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Task.CompletedTask;
            if (OperatingSystem.IsBrowser()) return Task.CompletedTask;
            try
            {
                var psi = new ProcessStartInfo("rider64.exe", $"\"{path}\"") { UseShellExecute = true };
                Process.Start(psi);
            }
            catch { }
            return Task.CompletedTask;
        }

        private async Task OpenInRider()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selectedNodeFilePath))
                {
                    return;
                }
                await OpenInRiderLocal(selectedNodeFilePath);
                return;

                //var normalized = selectedNodeFilePath.Replace('\\', '/');
                //var candidates = new string[]
                //{
                //    $"jetbrains://rider/open?file={normalized}",
                //    $"jetbrains://open?ide=rider&file={normalized}",
                //    $"rider://open?file={normalized}"
                //};

                //foreach (var uri in candidates)
                //{
                //    try
                //    {
                //        await JSRuntime.InvokeVoidAsync("open", uri, "_self");
                //        return;
                //    }
                //    catch { }
                //}
            }
            catch
            {
            }

            //try
            //{
            //    await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", selectedNodeFilePath);
            //}
            //catch { }
        }


        // $"<div df-data style=\"text-align: center;cursor: pointer;\">{nodeInput.Name}<br>({nodeInput.Data.Type})</div>";
        public async Task UpdateNodeHTMLAsync(string nodeName, string html)
        {
            if (!engine.ContainsNode(nodeName))
                return;
            string nodeId = GetFlowIndexStr(engine[nodeName].FlowID);
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

        private string inputName = "";

        private string tempNodeId = "";

        private string? draggingType;
        private double dragStartOffsetX;
        private double dragStartOffsetY;

        private void OnNodeDragStart(DragEventArgs e, string nodeType)
        {
            draggingType = nodeType;
            dragStartOffsetX = e.OffsetX;
            dragStartOffsetY = e.OffsetY;
        }

        private async Task DropAsync(DragEventArgs args)
        {
            var nodeType = draggingType;
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
                offsetX: dragStartOffsetX,
                offsetY: dragStartOffsetY,
                className: nodeTypes[nodeType],
                data: new NodeData
                {
                    Type = nodeType,
                    Color = nodeTypes[nodeType],
                    Name = nodeType + nodeNameSuffix,
                    Message = $"message from {nodeType}!",
                },
                html: $"<div df-data style=\"text-align: center;cursor: pointer; text-overflow: ellipsis; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; overflow: hidden;\">{nodeType + nodeNameSuffix}<br>({nodeType})</div>");

            draggingType = null;
        }

        public async Task ZoomAsync(double zoom)
        {
            if (_drawflow is null)
                return;
            await _drawflow.ZoomAsync(zoom);
        }

        private async Task NodeMoved(string nodeId)
        {
            if (await _drawflow.GetNodeFromIdAsync<NodeData>(nodeId) is StateMachineFlowNode<NodeData> node
                && node.Data != null && node.Name != null)
            {
                engine[node.Name].PosX = node.Pos_X;
                engine[node.Name].PosY = node.Pos_Y;
            }
        }

    }

}
