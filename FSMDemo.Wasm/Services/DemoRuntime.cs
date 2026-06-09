using System.Reflection;
using StateMachine;

namespace FSMDemo.Wasm.Services;

public sealed class DemoRuntime : IDisposable
{
    private readonly BrowserNodeFactory nodeFactory = new();
    private FSMEngine engine;
    private FSMExecutor? executor;

    public DemoRuntime()
    {
        engine = CreateEngine();
        ImportScript(DemoScript);
    }

    public event Action? Changed;

    public string Script { get; private set; } = DemoScript;
    public string? Error { get; private set; }
    public string State { get; private set; } = FSMState.Initialized.ToString();
    public string? ActiveNode { get; private set; }
    public string? ViewModule { get; private set; }
    public List<string> Logs { get; } = [];

    public IReadOnlyList<FsmNodeView> Nodes => BuildNodes();

    public IReadOnlyList<FsmConnectionView> Connections => BuildConnections();

    private IReadOnlyList<FsmNodeView> BuildNodes()
    {
        if (!string.IsNullOrWhiteSpace(ViewModule))
        {
            var prefix = ViewModule + ".";
            return engine.GetNodeNames()
                .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(name => engine[name])
                .Select(node => new FsmNodeView(
                    node.Name,
                    node.Name[prefix.Length..],
                    string.IsNullOrWhiteSpace(node.ClassType) ? node.GetType().Name : node.ClassType,
                    node.PosX,
                    node.PosY,
                    node.Color,
                    false,
                    node.EventDescriptions.Select(e => $"{e.Index}: {e.Description}").ToArray()))
                .OrderBy(node => node.Name)
                .ToArray();
        }

        var modulePrefixes = engine.ModuleInstances.Keys
            .Select(name => name + ".")
            .ToArray();

        var regularNodes = engine.GetNodeNames()
            .Where(name => !modulePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Select(name => engine[name])
            .Select(node => new FsmNodeView(
                node.Name,
                node.Name,
                string.IsNullOrWhiteSpace(node.ClassType) ? node.GetType().Name : node.ClassType,
                node.PosX,
                node.PosY,
                node.Color,
                false,
                node.EventDescriptions.Select(e => $"{e.Index}: {e.Description}").ToArray()));

        var moduleNodes = engine.ModuleInstances.Values
            .Select(instance => new FsmNodeView(
                instance.InstanceName,
                instance.InstanceName,
                instance.ModuleName,
                instance.PosX,
                instance.PosY,
                instance.Color,
                true,
                instance.OutputEventMap.Select(map => $"{map.Key}: {map.Value}").ToArray()));

        return regularNodes
            .Concat(moduleNodes)
            .OrderBy(node => node.Name)
            .ToArray();
    }

    private void ApplyDisplayLayout()
    {
        var rawNodes = BuildNodes();
        var connections = BuildConnections().ToArray();
        var layers = rawNodes.ToDictionary(node => node.Name, _ => int.MinValue);
        if (layers.ContainsKey("Start"))
        {
            layers["Start"] = 0;
        }

        foreach (var node in rawNodes.Where(node => !connections.Any(connection => connection.Target == node.Name)))
        {
            layers[node.Name] = Math.Max(layers[node.Name], 0);
        }

        for (var i = 0; i < rawNodes.Count; i++)
        {
            foreach (var connection in connections)
            {
                if (!layers.TryGetValue(connection.Source, out var sourceLayer) || sourceLayer == int.MinValue)
                {
                    continue;
                }

                if (layers.ContainsKey(connection.Target))
                {
                    layers[connection.Target] = Math.Max(layers[connection.Target], sourceLayer + 1);
                }
            }
        }

        foreach (var node in rawNodes)
        {
            if (layers[node.Name] == int.MinValue)
            {
                layers[node.Name] = 0;
            }
        }

        foreach (var positioned in rawNodes
            .GroupBy(node => layers[node.Name])
            .OrderBy(group => group.Key)
            .SelectMany(group => group.OrderBy(node => node.Name).Select((node, index) => new
            {
                NodeName = node.Name,
                node.IsModuleInstance,
                PosX = 60 + group.Key * 180,
                PosY = 80 + index * 145
            })))
        {
            if (positioned.IsModuleInstance && engine.ModuleInstances.TryGetValue(positioned.NodeName, out var instance))
            {
                instance.PosX = positioned.PosX;
                instance.PosY = positioned.PosY;
            }
            else if (engine.TryGetNode(positioned.NodeName, out var node))
            {
                node.PosX = positioned.PosX;
                node.PosY = positioned.PosY;
            }
        }
    }

    private IReadOnlyList<FsmConnectionView> BuildConnections()
    {
        var rawConnections = engine.GetNodeNames()
            .SelectMany(name => engine[name].GetFSMTransitions().Select(t =>
                new FsmConnectionView(name, t.Target.Name, t.Trigger.EventName)));

        if (!string.IsNullOrWhiteSpace(ViewModule))
        {
            var prefix = ViewModule + ".";
            return rawConnections
                .Where(connection =>
                    connection.Source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && connection.Target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(connection => connection.Source)
                .ThenBy(connection => connection.Target)
                .ToArray();
        }

        return rawConnections
            .Select(connection => connection with
            {
                Source = CollapseModuleNodeName(connection.Source),
                Target = CollapseModuleNodeName(connection.Target)
            })
            .Where(connection => !string.Equals(connection.Source, connection.Target, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(connection => connection.Source)
            .ThenBy(connection => connection.Target)
            .ToArray();
    }

    private string CollapseModuleNodeName(string nodeName)
    {
        foreach (var instanceName in engine.ModuleInstances.Keys.OrderByDescending(name => name.Length))
        {
            if (nodeName.StartsWith(instanceName + ".", StringComparison.OrdinalIgnoreCase))
            {
                return instanceName;
            }
        }

        return nodeName;
    }

    public IReadOnlyList<NodeTypeView> NodeTypes =>
        typeof(StartNode).Assembly.GetTypes()
            .Concat(typeof(FSMEngine).Assembly.GetTypes())
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && typeof(IFSMNode).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<FSMNodeAttribute>())
            .Where(attr => attr is not null)
            .Select(attr => new NodeTypeView(attr!.Key, attr.NodeDescription ?? "", attr.EventDescriptions ?? []))
            .OrderBy(type => type.Key)
            .ToArray();

    public void LoadDemo()
    {
        ImportScript(DemoScript);
    }

    public void ImportScript(string script)
    {
        executor?.Dispose();
        executor = null;
        ActiveNode = null;
        ViewModule = null;
        State = FSMState.Initialized.ToString();
        Logs.Clear();
        Error = null;

        try
        {
            var nextEngine = CreateEngine();
            nextEngine.CreateStateMachine(script);
            engine = nextEngine;
            ApplyDisplayLayout();
            Script = script;
            Logs.Add($"Imported {engine.GetNodeNames().Count()} nodes and {Connections.Count} connections.");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Logs.Add($"Import failed: {ex.Message}");
        }

        NotifyChanged();
    }

    public void MoveNodeBy(string nodeName, double deltaX, double deltaY)
    {
        if (engine.ModuleInstances.TryGetValue(nodeName, out var instance))
        {
            instance.PosX = Math.Clamp(instance.PosX + deltaX, 0, 970);
            instance.PosY = Math.Clamp(instance.PosY + deltaY, 0, 498);
            NotifyChanged();
            return;
        }

        if (!engine.TryGetNode(nodeName, out var node))
        {
            return;
        }

        node.PosX = Math.Clamp(node.PosX + deltaX, 0, 970);
        node.PosY = Math.Clamp(node.PosY + deltaY, 0, 498);
        NotifyChanged();
    }

    public bool EnterModule(string moduleInstanceName)
    {
        if (!engine.ModuleInstances.ContainsKey(moduleInstanceName))
        {
            return false;
        }

        ViewModule = moduleInstanceName;
        NotifyChanged();
        return true;
    }

    public void ExitModule()
    {
        ViewModule = null;
        NotifyChanged();
    }

    public async Task StartAsync(string startNodeName = "Start", string endEventName = "EndEvent")
    {
        if (!engine.TryGetNode(startNodeName, out var startNode))
        {
            Error = $"Start node '{startNodeName}' was not found.";
            NotifyChanged();
            return;
        }

        if (!engine.TryGetEvent(endEventName, out var endEvent))
        {
            Error = $"End event '{endEventName}' was not found.";
            NotifyChanged();
            return;
        }

        executor?.Dispose();
        executor = new FSMExecutor(startNode, endEvent);
        executor.NodeStateChanged += OnNodeEntered;
        executor.NodeExitChanged += OnNodeExited;
        executor.FSMStateChanged += OnStateChanged;

        Error = null;
        ActiveNode = null;
        Logs.Clear();
        Logs.Add($"Starting at {startNodeName}, ending on {endEventName}.");
        await executor.RestartAsync();
        NotifyChanged();
    }

    public async Task PauseAsync()
    {
        if (executor is null)
        {
            return;
        }

        await executor.PauseAsync();
        NotifyChanged();
    }

    public void Continue()
    {
        executor?.Continue();
        NotifyChanged();
    }

    public async Task StopAsync()
    {
        if (executor is null)
        {
            return;
        }

        await executor.StopAsync();
        ActiveNode = null;
        NotifyChanged();
    }

    private FSMEngine CreateEngine()
    {
        return FSMEngineBuilder.Create().ConfigureNodeFactory(nodeFactory).Build();
    }

    private void OnNodeEntered(object? sender, string nodeName)
    {
        ActiveNode = nodeName;
        Logs.Add($"Enter {nodeName}");
        NotifyChanged();
    }

    private void OnNodeExited(object? sender, string nodeName)
    {
        Logs.Add($"Exit {nodeName}");
        NotifyChanged();
    }

    private void OnStateChanged(FSMExecutor source, FSMState current, FSMState previous)
    {
        State = current.ToString();
        if (current is FSMState.Finished or FSMState.Stoped)
        {
            ActiveNode = null;
        }

        Logs.Add($"State {previous} -> {current}");
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (Logs.Count > 80)
        {
            Logs.RemoveRange(0, Logs.Count - 80);
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        executor?.Dispose();
    }

    public const string DemoScript = """
module Checker {
    input Entry;
    output OK, NG;

    def Entry(Accumulate)
    {
        1->NextEvent;
        3->NG;
        Pos:(100, 200);
        Color: "light-blue";
    }

    def Exit(Idle)
    {
        1->OK;
        Pos:(300, 200);
        Color: "green";
    }

    def NextEvent as event;
    def OK as event;
    def NG as event;

    NextEvent->Entry to Exit;
}

import Checker;

def Start(Start)
{
    1->StartEvent;
    Pos:(100, 200);
    Color: "red";
}

def CheckA(Checker)
{
    0->CheckAOK;
    1->CheckANG;
    Pos:(400, 100);
    Color: "orange";
}

def CheckB(Checker)
{
    0->CheckBOK;
    1->CheckBNG;
    Pos:(400, 350);
    Color: "teal";
}

def End(End)
{
    1->EndEvent;
    Pos:(700, 200);
    Color: "indigo";
}

def ErrorEnd(End)
{
    1->ErrorEndEvent;
    Pos:(700, 450);
    Color: "red";
}

StartEvent->Start to CheckA.Entry;
CheckAOK->CheckA to CheckB.Entry;
CheckANG->CheckA to ErrorEnd;
CheckBOK->CheckB to End;
CheckBNG->CheckB to ErrorEnd;
""";
}

public sealed record FsmNodeView(
    string Name,
    string DisplayName,
    string Type,
    double PosX,
    double PosY,
    string Color,
    bool IsModuleInstance,
    IReadOnlyList<string> Events);

public sealed record FsmConnectionView(string Source, string Target, string EventName);

public sealed record NodeTypeView(string Key, string Description, IReadOnlyList<string> Events);
