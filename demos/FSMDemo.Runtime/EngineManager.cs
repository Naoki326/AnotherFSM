using System.Reflection;
using Microsoft.Extensions.Logging;
using StateMachine;
using StateMachine.Interface;

namespace FSMDemo.Runtime;

public partial class EngineManager : IAsyncDisposable
{
    private readonly object _executionSync = new();
    private long _executionVersion;
    private bool _startPending;
    private TaskCompletionSource? _starting;
    private sealed class StartRetiredException : Exception { }
    private bool _disposed;
    private Task _retirement = Task.CompletedTask;
    private EditingEngine? _engine;
    private FSMExecutor? _executor;
    private readonly ILogger<EngineManager> _logger;
    private readonly IFSMNodeFactory _nodeFactory;
    private readonly bool _browser;
    private readonly HostContext? _context;
    // Snapshot the expanded template edges, independently of in-progress template edits.
    private readonly HashSet<SavedConnection> _templateEdges = [];

    public FSMEngine Engine => _engine ??= new EditingEngine(_nodeFactory) { AllowExternalModuleFiles = !_browser };
    private EditingEngine EditableEngine => (EditingEngine)Engine;
    public FSMExecutor? Executor => _executor;
    public ExecutionState CurrentState { get; private set; } = ExecutionState.Idle;
    public string? CurrentNodeName { get; private set; }

    public EngineManager(ILogger<EngineManager> logger, IFSMNodeFactory nodeFactory, bool browser = false, HostContext? context = null)
        => (_logger, _nodeFactory, _browser, _context) = (logger, nodeFactory, browser, context);

    // Keep expansion in the core DSL builder. This adapter only installs the resulting
    // flat objects, canonicalizing events already used by external transitions.
    private sealed class EditingEngine(IFSMNodeFactory factory) : FSMEngine(factory)
    {
        public static EditingEngine Expand(IFSMNodeFactory factory, FsmModuleInfo template, string moduleName, string instanceName, bool browser)
        {
            var staged = new EditingEngine(factory) { AllowExternalModuleFiles = !browser };
            var loadScript = template.TemplateNodes.Count == 0 && template.TemplateEvents.Count == 0
                && template.Inputs.Count == 0 && template.Outputs.Count == 0 && template.PendingTransitions.Count == 0
                && !string.IsNullOrEmpty(template.ScriptContent);
            staged.RegisterModule(moduleName, loadScript ? template.ScriptContent : "", template.DirectoryPath);
            if (!loadScript)
            {
                staged.TryGetModule(moduleName, out var cached);
                cached.ModuleName = template.ModuleName;
                cached.TemplateNodes = template.TemplateNodes;
                cached.TemplateEvents = template.TemplateEvents;
                cached.Inputs = template.Inputs;
                cached.Outputs = template.Outputs;
                cached.PendingTransitions = template.PendingTransitions;
            }
            staged.CreateStateMachine($"use {moduleName} as {instanceName};");
            if (loadScript)
            {
                staged.TryGetModule(moduleName, out var parsed);
                template.TemplateNodes = parsed.TemplateNodes;
                template.TemplateEvents = parsed.TemplateEvents;
                template.Inputs = parsed.Inputs;
                template.Outputs = parsed.Outputs;
                template.PendingTransitions = parsed.PendingTransitions;
            }
            return staged;
        }

        public void Install(EditingEngine staged)
        {
            foreach (var ev in staged.eventDict.Values) TryAddEvent(ev);
            // Staging is another engine: install definitions, never its Node instances.
            foreach (var source in staged.nodeDict.Values)
            {
                CreateNode(source.ClassType, source.Name);
                var target = GetNode(source.Name);
                target.NamePrefix = source.NamePrefix;
                target.PosX = source.PosX;
                target.PosY = source.PosY;
                target.Color = source.Color;
                target.FlowID = source.FlowID;
                foreach (var property in source.GetType().GetProperties()
                    .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                        && p.GetCustomAttribute<FSMPropertyAttribute>() != null))
                    property.SetValue(target, property.GetValue(source));
                source.UpdateEventDescriptions();
                foreach (var branch in source.EventDescriptions)
                    target.SetBranchEvent(branch.Index, GetEvent(branch.Description));
            }
            foreach (var source in staged.nodeDict.Values)
                foreach (var transition in source.GetFSMTransitions())
                    GetNode(source.Name).AddTransition(GetEvent(transition.Trigger.EventName), GetNode(transition.Target.Name));
            foreach (var pair in staged.ModuleInstances) AddModuleInstance(pair.Key, pair.Value);
        }

        public void RemoveOwnedEvents(IEnumerable<string> names)
        {
            foreach (var name in names) eventDict.Remove(name);
        }
    }

    private sealed record ModuleNodeRef(string InstanceName, FsmModuleInstance Instance, FsmModuleInfo Module, string LocalName);
    private readonly record struct SavedConnection(string FromNodeName, string ToNodeName, string EventName);

    public EngineStatus GetStatus() => new()
    {
        NodeNames = Engine.GetNodeNames().ToList(),
        EventNames = Engine.GetEventNames().ToList(),
        ConnectionCount = GetConnections().Count,
    };

    public void Import(string script)
    {
        var candidate = new EditingEngine(_nodeFactory) { AllowExternalModuleFiles = !_browser };
        candidate.CreateStateMachine(script);
        ValidateHostContext(candidate);
        lock (_executionSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RetireExecution();
            var previous = _engine;
            _engine = candidate;
            if (previous != null) _retirement = CleanupGraphAsync(_retirement, previous);
        }
        _templateEdges.Clear();
        foreach (var instance in candidate.ModuleInstances.Values)
        {
            candidate.TryGetModule(instance.ModuleName, out var template);
            var prefix = instance.InstanceName + ".";
            foreach (var edge in template.PendingTransitions)
                _templateEdges.Add(new(prefix + edge.SourceNode, prefix + edge.TargetNode, prefix + edge.EventName));
        }
    }

    public async Task ImportAsync(string script)
    {
        Import(script);
        Task retirement;
        lock (_executionSync) retirement = _retirement;
        await retirement;
    }

    private bool IsExpandedTemplateEdge(FSMTransition edge)
        => _templateEdges.Contains(new(edge.Source.Name, edge.Target.Name, edge.Trigger.EventName));

    private void InstallExpansion(EditingEngine staged)
    {
        foreach (var edge in staged.SelectMany(node => node.GetFSMTransitions()))
            _templateEdges.Add(new(edge.Source.Name, edge.Target.Name, edge.Trigger.EventName));
        EditableEngine.Install(staged);
    }

    private FsmModuleInstance? GetNodeOwner(string name)
        => Engine.ModuleInstances.Values.SingleOrDefault(instance => instance.ExpandedNodeNames.Contains(name));

    private ModuleNodeRef? GetModuleNodeRef(string nodeName)
    {
        var instance = GetNodeOwner(nodeName);
        if (instance == null || !Engine.TryGetModule(instance.ModuleName, out var module)) return null;
        return new(instance.InstanceName, instance, module, nodeName[(instance.InstanceName.Length + 1)..]);
    }

    private bool TryGetSameInstanceInternalNodes(string from, string to, out ModuleNodeRef fromRef, out ModuleNodeRef toRef)
    {
        fromRef = GetModuleNodeRef(from)!;
        toRef = GetModuleNodeRef(to)!;
        return fromRef != null && toRef != null && fromRef.InstanceName == toRef.InstanceName;
    }

    private static string NormalizeTemplateNodeName(ModuleNodeRef moduleNode, string name)
    {
        var prefix = moduleNode.InstanceName + ".";
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
    }

    private static FSMEvent EnsureTemplateEvent(FsmModuleInfo module, string eventName)
    {
        if (!module.TemplateEvents.TryGetValue(eventName, out var ev))
            module.TemplateEvents.Add(eventName, ev = new FSMEvent(eventName));
        return ev;
    }

    private FSMEvent EnsureEngineEvent(string eventName)
    {
        if (!Engine.TryGetEvent(eventName, out var ev)) Engine.AddEvent(ev = new FSMEvent(eventName));
        return ev;
    }

    private bool TryResolveConnectionSourceNodes(string from, string eventName, out List<string> names, out string error)
    {
        names = [];
        error = "";
        try
        {
            if (Engine.ModuleInstances.TryGetValue(from, out var instance))
            {
                // The service contract uses instance name + qualified output event.
                var output = Engine.ModuleInterfaces.ResolveOutput(eventName);
                if (!instance.PublicOutputs.Values.Contains(output.Event.EventName, StringComparer.Ordinal)
                    || output.Publishers.Any(node => !instance.ExpandedNodeNames.Contains(node.Name)))
                    throw new ScriptException($"Module instance '{from}' does not own output '{eventName}'");
                names = output.Publishers.Select(node => node.Name).Distinct(StringComparer.Ordinal).ToList();
                return true;
            }
            if (GetNodeOwner(from) != null)
                throw new ScriptException($"Module internal source '{from}' is private; select the instance public output");
            if (Engine.ModuleInstances.Values.Any(i => i.ExpandedEventNames.Contains(eventName)))
                throw new ScriptException($"Module event '{eventName}' must be connected through its public output");
            if (!Engine.ContainsNode(from)) throw new ScriptException($"Node or module instance '{from}' not found");
            names = [from];
            return true;
        }
        catch (ScriptException ex) { error = ex.Message; return false; }
    }

    private bool TryResolveConnectionTargetNodes(string to, out List<string> names, out string error)
    {
        names = [];
        error = "";
        try
        {
            if (Engine.ModuleInstances.ContainsKey(to) || GetNodeOwner(to) != null || !Engine.ContainsNode(to))
                names = [Engine.ModuleInterfaces.ResolveInput(to).Name];
            else names = [to];
            return true;
        }
        catch (ScriptException ex) { error = ex.Message; return false; }
    }

    private string PublicTarget(FsmModuleInstance instance, string nodeName)
    {
        var inputs = instance.PublicInputs.Where(pair => pair.Value == nodeName).ToList();
        if (inputs.Count != 1)
            throw new InvalidOperationException($"Module target '{nodeName}' is not an unambiguous public input");
        var reference = instance.PublicInputs.Count == 1 ? instance.InstanceName : instance.InstanceName + "." + inputs[0].Key;
        if (Engine.ModuleInterfaces.ResolveInput(reference).Name != nodeName)
            throw new InvalidOperationException($"Module target '{reference}' has conflicting input identity");
        return reference;
    }

    private SavedConnection ProjectConnection(FSMTransition transition)
    {
        var from = transition.Source.Name;
        var to = transition.Target.Name;
        var sourceOwner = GetNodeOwner(from);
        var targetOwner = GetNodeOwner(to);
        if (sourceOwner != null && ReferenceEquals(sourceOwner, targetOwner) && IsExpandedTemplateEdge(transition))
            return new(from, to, transition.Trigger.EventName);
        if (sourceOwner != null)
        {
            var output = sourceOwner.PublicOutputs.SingleOrDefault(pair => pair.Value == transition.Trigger.EventName);
            if (output.Key == null)
                throw new InvalidOperationException($"Module source '{from}' does not publish a public output '{transition.Trigger.EventName}'");
            var resolved = Engine.ModuleInterfaces.ResolveOutput(sourceOwner.InstanceName + "." + output.Key);
            if (!resolved.Publishers.Any(node => node.Name == from))
                throw new InvalidOperationException($"Module source '{from}' is not an actual publisher of '{transition.Trigger.EventName}'");
            from = sourceOwner.InstanceName;
        }
        if (targetOwner != null) to = PublicTarget(targetOwner, to);
        return new(from, to, transition.Trigger.EventName);
    }

    public List<ConnectionDto> GetConnections()
        => Engine.SelectMany(node => node.GetFSMTransitions()).Select(ProjectConnection).Distinct()
            .Select(edge => new ConnectionDto { FromNodeName = edge.FromNodeName, ToNodeName = edge.ToNodeName, EventName = edge.EventName }).ToList();

    private void RemoveExpandedNodes(IEnumerable<FsmModuleInstance> instances)
    {
        var names = instances.SelectMany(instance => instance.ExpandedNodeNames).ToHashSet(StringComparer.Ordinal);
        foreach (var node in Engine.ToList())
            foreach (var transition in node.GetFSMTransitions().Where(t => names.Contains(t.Target.Name)).ToList())
                node.DeleteTransition(transition.Trigger);
        foreach (var name in names)
        {
            if (Engine.TryGetNode(name, out var node)) node.ClearTransition();
            Engine.TryDeleteNode(name);
        }
        _templateEdges.RemoveWhere(edge => names.Contains(edge.FromNodeName));
        foreach (var instance in instances) Engine.RemoveModuleInstance(instance.InstanceName);
    }

    private void RebuildModuleInstances(string moduleName)
    {
        if (!Engine.TryGetModule(moduleName, out var template)) return;
        var instances = Engine.ModuleInstances.Values.Where(i => string.Equals(i.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (instances.Count == 0) return;
        var affected = instances.Select(i => i.InstanceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var external = Engine.SelectMany(node => node.GetFSMTransitions())
            .Where(t => {
                var source = GetNodeOwner(t.Source.Name);
                var target = GetNodeOwner(t.Target.Name);
                return !IsExpandedTemplateEdge(t) && ((source != null && affected.Contains(source.InstanceName))
                    || (target != null && affected.Contains(target.InstanceName)));
            }).Select(ProjectConnection).Distinct().ToList();
        // Build before removing anything: invalid templates cannot destroy the flat graph.
        var expansions = instances.Select(i => (Original: i, Staged: EditingEngine.Expand(_nodeFactory, template, moduleName, i.InstanceName, _browser))).ToList();
        var stagedByName = expansions.ToDictionary(item => item.Original.InstanceName, item => item.Staged, StringComparer.OrdinalIgnoreCase);
        var planned = new HashSet<(string Source, string Event)>();
        foreach (var edge in external)
        {
            if (stagedByName.TryGetValue(edge.FromNodeName, out var stagedSource))
            {
                var output = stagedSource.ModuleInterfaces.ResolveOutput(edge.EventName);
                foreach (var publisher in output.Publishers)
                    if (publisher.HasTransition(output.Event) || !planned.Add((publisher.Name, output.Event.EventName)))
                        throw new ScriptException($"Template edit conflicts with public output connection '{edge.EventName}'");
            }
            if (!TryResolveConnectionTargetNodes(edge.ToNodeName, out var targets, out var error)) throw new ScriptException(error);
            var targetOwner = GetNodeOwner(targets.Single());
            if (targetOwner != null && stagedByName.TryGetValue(targetOwner.InstanceName, out var stagedTarget))
                stagedTarget.ModuleInterfaces.ResolveInput(edge.ToNodeName);
        }
        foreach (var (_, staged) in expansions)
            foreach (var node in staged)
                if (Engine.TryGetNode(node.Name, out var previous)) node.NamePrefix = previous.NamePrefix;
        RemoveExpandedNodes(instances);
        foreach (var (original, staged) in expansions)
        {
            var replacement = staged.ModuleInstances[original.InstanceName];
            replacement.PosX = original.PosX; replacement.PosY = original.PosY;
            replacement.Color = original.Color; replacement.FlowID = original.FlowID;
            InstallExpansion(staged);
            EditableEngine.RemoveOwnedEvents(original.ExpandedEventNames.Except(replacement.ExpandedEventNames));
        }
        foreach (var edge in external)
        {
            var result = CreateConnection(edge.FromNodeName, edge.ToNodeName, edge.EventName);
            if (!result.Success) throw new InvalidOperationException($"Cannot restore public connection: {result.Error}");
        }
        Engine.ReinitGroupNode();
    }

    private ModuleInstanceDto BuildModuleInstanceDto(FsmModuleInstance instance)
    {
        var publishers = new Dictionary<string, string[]>();
        foreach (var output in instance.PublicOutputs)
        {
            // An output with no publisher is legal to declare, but cannot be wired.
            try { publishers[output.Key] = Engine.ModuleInterfaces.ResolveOutput(instance.InstanceName + "." + output.Key).Publishers.Select(n => n.Name).ToArray(); }
            catch (ScriptException) { publishers[output.Key] = []; }
        }
        return new ModuleInstanceDto
        {
            InstanceName = instance.InstanceName, ModuleName = instance.ModuleName,
            PosX = instance.PosX, PosY = instance.PosY, Color = instance.Color, FlowID = instance.FlowID,
            InternalNodeNames = instance.ExpandedNodeNames.ToList(),
            PublicInputs = new(instance.PublicInputs), PublicOutputs = new(instance.PublicOutputs), OutputPublishers = publishers,
        };
    }

    public List<NodeInfo> GetNodes() => Engine.GetNodeNames().Select(name => GetNode(name)!).ToList();

    public NodeInfo? GetNode(string name)
    {
        if (!Engine.TryGetNode(name, out var node)) return null;
        node.UpdateEventDescriptions();
        return new NodeInfo
        {
            Name = node.Name, ClassType = node.ClassType ?? "", Color = node.Color ?? "",
            PosX = node.PosX, PosY = node.PosY, FlowID = node.FlowID ?? "", Discription = node.Discription,
            EventDescriptions = node.EventDescriptions?.Select(ed => new NodeEventDescriptionDto { Index = ed.Index, Description = ed.Description }).ToList() ?? [],
        };
    }

    public NodeInfo CreateNode(string type, string name, double posX, double posY, string color = "")
    {
        var actualName = name;
        var suffix = 1;
        while (Engine.ContainsNode(actualName) || Engine.ModuleInstances.ContainsKey(actualName)) actualName = $"{name}{suffix++}";
        Engine.CreateNode(type, actualName);
        var node = Engine[actualName];
        node.PosX = posX; node.PosY = posY; node.ClassType = type;
        if (!string.IsNullOrEmpty(color)) node.Color = color;
        if (node.GetType().GetCustomAttributes(typeof(FSMNodeAttribute), true).FirstOrDefault() is FSMNodeAttribute info
            && info.Indexes.Length == info.EventDescriptions.Length)
        {
            node.EventDescriptions = Enumerable.Range(0, info.Indexes.Length)
                .Select(i => new NodeEventDescription { Index = info.Indexes[i], Description = info.EventDescriptions[i] }).ToList();
            foreach (var ed in node.EventDescriptions) node.SetBranchEvent(ed.Index, EnsureEngineEvent(ed.Description));
        }
        return GetNode(actualName)!;
    }

    public bool DeleteNode(string name)
    {
        if (!Engine.ContainsNode(name) || GetNodeOwner(name) != null) return false;
        foreach (var node in Engine.ToList()) node.DeleteTransition(Engine[name]);
        Engine[name].ClearTransition();
        Engine.TryDeleteNode(name);
        return true;
    }

    public bool RenameNode(string oldName, string newName)
        => GetNodeOwner(oldName) == null && !Engine.ModuleInstances.ContainsKey(newName) && Engine.TryChangeNodeName(oldName, newName);

    public bool UpdateNodePosition(string name, double posX, double posY)
    {
        var moduleNode = GetModuleNodeRef(name);
        if (moduleNode != null)
        {
            if (!moduleNode.Module.TemplateNodes.TryGetValue(moduleNode.LocalName, out var template)) return false;
            template.PosX = posX; template.PosY = posY;
            RebuildModuleInstances(moduleNode.Instance.ModuleName);
            return true;
        }
        if (!Engine.TryGetNode(name, out var node)) return false;
        node.PosX = posX; node.PosY = posY;
        return true;
    }

    public bool UpdateNodeEvent(string name, int index, string newEventName)
    {
        if (Engine.ModuleInstances.ContainsKey(name)) return false;
        var moduleNode = GetModuleNodeRef(name);
        if (moduleNode != null)
        {
            if (!moduleNode.Module.TemplateNodes.TryGetValue(moduleNode.LocalName, out var template)) return false;
            template.UpdateEventDescriptions();
            var oldBranch = template.EventDescriptions.FirstOrDefault(branch => branch.Index == index)?.Description;
            var localEvent = NormalizeTemplateNodeName(moduleNode, newEventName);
            var hadEvent = moduleNode.Module.TemplateEvents.ContainsKey(localEvent);
            template.SetBranchEvent(index, EnsureTemplateEvent(moduleNode.Module, localEvent));
            template.UpdateEventDescriptions();
            try { RebuildModuleInstances(moduleNode.Instance.ModuleName); }
            catch (ScriptException)
            {
                if (oldBranch == null) template.DeleteBranch(index);
                else template.SetBranchEvent(index, EnsureTemplateEvent(moduleNode.Module, oldBranch));
                template.UpdateEventDescriptions();
                if (!hadEvent) moduleNode.Module.TemplateEvents.Remove(localEvent);
                return false;
            }
            return true;
        }
        if (!Engine.TryGetNode(name, out var node)) return false;
        // 与 Namespace 内 DSL Branch 声明一致：编辑相对名称时保留其命名范围。
        if (!newEventName.StartsWith(node.NamePrefix, StringComparison.Ordinal))
            newEventName = node.NamePrefix + newEventName;
        if (Engine.ModuleInstances.Values.Any(i => i.ExpandedEventNames.Contains(newEventName))) return false;
        node.SetBranchEvent(index, EnsureEngineEvent(newEventName));
        node.UpdateEventDescriptions();
        return true;
    }

    // Retained as a disabled service seam for older callers; no terminal semantics remain.
    public bool UpdateModuleTerminal(string name, bool isTerminal) => false;

    public (bool Success, string Error) CreateConnection(string fromNodeName, string toNodeName, string eventName)
    {
        if (TryGetSameInstanceInternalNodes(fromNodeName, toNodeName, out var from, out var to))
        {
            var localEvent = NormalizeTemplateNodeName(from, eventName);
            if (from.Module.PendingTransitions.Any(t => t.SourceNode == from.LocalName && t.EventName == localEvent))
                return (false, $"Connection from '{fromNodeName}' using event '{eventName}' already exists");
            var hadEvent = from.Module.TemplateEvents.ContainsKey(localEvent);
            EnsureTemplateEvent(from.Module, localEvent);
            var added = new TemplateTransition { SourceNode = from.LocalName, TargetNode = to.LocalName, EventName = localEvent };
            from.Module.PendingTransitions.Add(added);
            try { RebuildModuleInstances(from.Instance.ModuleName); }
            catch (ScriptException ex)
            {
                from.Module.PendingTransitions.Remove(added);
                if (!hadEvent) from.Module.TemplateEvents.Remove(localEvent);
                return (false, ex.Message);
            }
            return (true, "");
        }
        if (!TryResolveConnectionSourceNodes(fromNodeName, eventName, out var sources, out var error)) return (false, error);
        if (!TryResolveConnectionTargetNodes(toNodeName, out var targets, out error)) return (false, error);
        foreach (var source in sources)
            if (Engine[source].GetFSMTransitions().Any(t => t.Trigger.EventName == eventName))
                return (false, $"Connection from '{source}' using event '{eventName}' already exists");
        var ev = EnsureEngineEvent(eventName);
        foreach (var source in sources) Engine[source].AddTransition(ev, Engine[targets.Single()]);
        return (true, "");
    }

    public bool DeleteConnection(string fromNodeName, string toNodeName, string? eventName = null)
    {
        if (TryGetSameInstanceInternalNodes(fromNodeName, toNodeName, out var from, out var to))
        {
            var localEvent = eventName == null ? null : NormalizeTemplateNodeName(from, eventName);
            var removed = from.Module.PendingTransitions.RemoveAll(t => t.SourceNode == from.LocalName && t.TargetNode == to.LocalName
                && (localEvent == null || t.EventName == localEvent));
            if (removed == 0) return false;
            RebuildModuleInstances(from.Instance.ModuleName);
            return true;
        }
        if (eventName == null && Engine.ModuleInstances.TryGetValue(fromNodeName, out var instance))
        {
            var removed = false;
            foreach (var output in instance.PublicOutputs.Values) removed |= DeleteConnection(fromNodeName, toNodeName, output);
            return removed;
        }
        if (!TryResolveConnectionSourceNodes(fromNodeName, eventName ?? "", out var sources, out _)
            || !TryResolveConnectionTargetNodes(toNodeName, out var targets, out _)) return false;
        var removedAny = false;
        foreach (var source in sources)
            foreach (var transition in Engine[source].GetFSMTransitions().Where(t => targets.Contains(t.Target.Name)
                && (eventName == null || t.Trigger.EventName == eventName)).ToList())
                removedAny |= Engine[source].DeleteTransition(transition.Trigger);
        return removedAny;
    }

    public bool RenameConnection(string fromNodeName, string toNodeName, string newEventName, string? eventName = null)
    {
        if (TryGetSameInstanceInternalNodes(fromNodeName, toNodeName, out var from, out var to))
        {
            var oldEvent = eventName == null ? null : NormalizeTemplateNodeName(from, eventName);
            var transition = from.Module.PendingTransitions.FirstOrDefault(t => t.SourceNode == from.LocalName && t.TargetNode == to.LocalName
                && (oldEvent == null || t.EventName == oldEvent));
            if (transition == null) return false;
            var localEvent = NormalizeTemplateNodeName(from, newEventName);
            if (from.Module.PendingTransitions.Any(t => !ReferenceEquals(t, transition) && t.SourceNode == from.LocalName && t.EventName == localEvent)) return false;
            var hadEvent = from.Module.TemplateEvents.ContainsKey(localEvent);
            EnsureTemplateEvent(from.Module, localEvent);
            var previous = transition.EventName;
            transition.EventName = localEvent;
            try { RebuildModuleInstances(from.Instance.ModuleName); }
            catch (ScriptException)
            {
                transition.EventName = previous;
                if (!hadEvent) from.Module.TemplateEvents.Remove(localEvent);
                return false;
            }
            return true;
        }
        if (Engine.ModuleInstances.ContainsKey(fromNodeName) || GetNodeOwner(fromNodeName) != null) return false;
        if (!TryResolveConnectionSourceNodes(fromNodeName, newEventName, out var sources, out _)
            || !TryResolveConnectionTargetNodes(toNodeName, out var targets, out _)) return false;
        var node = Engine[sources.Single()];
        var existing = node.GetFSMTransitions().Where(t => targets.Contains(t.Target.Name) && (eventName == null || t.Trigger.EventName == eventName)).ToList();
        if (existing.Count != 1 || node.GetFSMTransitions().Any(t => !ReferenceEquals(t, existing[0]) && t.Trigger.EventName == newEventName)) return false;
        node.DeleteTransition(existing[0].Trigger);
        node.AddTransition(EnsureEngineEvent(newEventName), existing[0].Target);
        return true;
    }

    public List<ModuleInstanceDto> GetModuleInstances() => Engine.ModuleInstances.Values.Select(BuildModuleInstanceDto).ToList();

    public List<ModuleDefDto> GetModuleDefinitions() => Engine.GetModuleNames().Select(name =>
    {
        Engine.TryGetModule(name, out var info);
        return new ModuleDefDto { ModuleName = name, Inputs = info.Inputs, Outputs = info.Outputs };
    }).ToList();

    public ModuleInstanceDto AddModuleInstance(string moduleName, string instanceName, double posX, double posY)
    {
        if (!Engine.TryGetModule(moduleName, out var template)) throw new InvalidOperationException($"Module '{moduleName}' not found");
        var staged = EditingEngine.Expand(_nodeFactory, template, moduleName, instanceName, _browser);
        var instance = staged.ModuleInstances[instanceName];
        if (Engine.ModuleInstances.ContainsKey(instanceName) || Engine.GetNodeNames().Contains(instanceName, StringComparer.OrdinalIgnoreCase)
            || instance.ExpandedNodeNames.Any(name => Engine.ContainsNode(name) || Engine.ModuleInstances.ContainsKey(name))
            || instance.ExpandedEventNames.Any(Engine.ContainsEvent))
            throw new InvalidOperationException($"Module instance '{instanceName}' name collision");
        instance.PosX = posX; instance.PosY = posY; instance.Color = "deep-purple";
        InstallExpansion(staged);
        Engine.ReinitGroupNode();
        return BuildModuleInstanceDto(instance);
    }

    public bool UpdateModuleInstancePosition(string instanceName, double posX, double posY)
    {
        if (!Engine.ModuleInstances.TryGetValue(instanceName, out var instance)) return false;
        instance.PosX = posX; instance.PosY = posY;
        return true;
    }

    public bool DeleteModuleInstance(string instanceName)
    {
        if (!Engine.ModuleInstances.TryGetValue(instanceName, out var instance)) return false;
        RemoveExpandedNodes([instance]);
        EditableEngine.RemoveOwnedEvents(instance.ExpandedEventNames);
        return true;
    }

    private void ValidateHostContext(FSMEngine engine)
    {
        if (_context != null) { _context.Validate(engine); return; }
        foreach (var name in engine.GetNodeNames())
            if (engine.GetNode(name) is not IFSMContextNode { ContextType: null })
                throw new FSMException($"Node {name}, requirement {(engine.GetNode(name) as IFSMContextNode)?.ContextType}: C# host aggregate/source pairing is missing.");
    }

    public async Task<bool> StartExecution(string startNodeName, string endEventName)
    {
        long version;
        FSMEngine graph;
        TaskCompletionSource starting;
        lock (_executionSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startPending || !_retirement.IsCompleted || (_executor?.ExecutorTask is { IsCompleted: false }))
                throw new FSMException("Engine has an unexited execution; conflicting start rejected. Stop and await exit first.");
            graph = Engine;
            if (!graph.ContainsNode(startNodeName))
            {
                _logger.LogWarning("Start node '{NodeName}' not found", startNodeName);
                return false;
            }
            _startPending = true;
            _starting = starting = new(TaskCreationOptions.RunContinuationsAsynchronously);
            version = _executionVersion;
        }
        try
        {
            // User source getters and node initialization must never hold the host
            // status lock. Explicit retirement waits for this startup barrier.
            ValidateHostContext(graph);
            if (!graph.TryGetEvent(endEventName, out var endEvent)) endEvent = new FSMEvent(endEventName);
            graph.ReinitGroupNode();
            void Observe(FSMExecutor value)
            {
                lock (_executionSync)
                {
                    if (_disposed || version != _executionVersion) throw new StartRetiredException();
                    _executor = value;
                    value.FSMStateChanged += OnFSMStateChanged;
                    value.NodeStateChanged += OnNodeStateChanged;
                    value.NodeExitChanged += OnNodeExitChanged;
                }
            }
            var ends = ResolveExecutionEndEvents(graph, endEvent);
            FSMExecutor executor;
            bool result;
            if (_context != null && graph.OfType<IFSMContextNode>().Any(node => node.ContextType != null))
            {
                using var prepared = _context.Prepare(graph);
                executor = await prepared.StartAsync(startNodeName, ends, Observe);
                result = true;
            }
            else
            {
                executor = new FSMExecutor(graph[startNodeName], ends);
                Observe(executor);
                result = await executor.RestartAsync();
            }
            lock (_executionSync) return result && version == _executionVersion && ReferenceEquals(_executor, executor);
        }
        catch (StartRetiredException) { return false; }
        finally
        {
            lock (_executionSync)
                if (ReferenceEquals(_starting, starting)) _startPending = false;
            starting.TrySetResult();
        }
    }

    private static List<FSMEvent> ResolveExecutionEndEvents(FSMEngine graph, FSMEvent requestedEndEvent)
    {
        var result = new List<FSMEvent> { requestedEndEvent };
        var seen = new HashSet<string>(StringComparer.Ordinal) { requestedEndEvent.EventID };
        foreach (var node in graph)
        {
            if (graph.ModuleInstances.Values.Any(i => i.ExpandedNodeNames.Contains(node.Name)) || !string.Equals(node.ClassType, "End", StringComparison.OrdinalIgnoreCase)) continue;
            node.UpdateEventDescriptions();
            foreach (var desc in node.EventDescriptions)
                if (graph.TryGetEvent(desc.Description, out var endEvent) && seen.Add(endEvent.EventID)) result.Add(endEvent);
        }
        return result;
    }

    public async Task<bool> PauseExecution()
    {
        FSMExecutor? executor;
        lock (_executionSync) executor = _executor;
        if (executor == null) return false;
        // Core notifications may synchronously read host status; never invert the locks.
        var result = await executor.PauseAsync();
        lock (_executionSync) return result && ReferenceEquals(_executor, executor);
    }

    public bool ContinueExecution()
    {
        FSMExecutor? executor;
        lock (_executionSync) executor = _executor;
        if (executor == null) return false;
        var result = executor.Continue();
        lock (_executionSync) return result && ReferenceEquals(_executor, executor);
    }

    // Called only under _executionSync. Detach first: even callbacks already queued by
    // an observer must prove ownership before publishing into the current graph.
    private void RetireExecution()
    {
        ++_executionVersion;
        if (_startPending && _starting != null) _retirement = Task.WhenAll(_retirement, _starting.Task);
        _startPending = false;
        var old = _executor;
        _executor = null;
        if (old != null)
        {
            old.FSMStateChanged -= OnFSMStateChanged;
            old.NodeStateChanged -= OnNodeStateChanged;
            old.NodeExitChanged -= OnNodeExitChanged;
            _retirement = CleanupExecutorAsync(_retirement, old);
        }
        CurrentState = ExecutionState.Idle;
        CurrentNodeName = null;
        if (old != null && !_disposed) StateChanged?.Invoke(CurrentState, null);
    }

    private async Task CleanupExecutorAsync(Task previous, FSMExecutor old)
    {
        // RetireExecution creates this task under the host lock. Leave that lock's
        // synchronous call stack before requesting core control (also on WASM).
        await Task.Yield();
        await previous;
        try { await old.StopAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to stop retired executor"); }
        finally { old.Dispose(); }
    }

    private async Task CleanupGraphAsync(Task retirement, EditingEngine graph)
    {
        await retirement;
        foreach (var node in graph)
        {
            try { node.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to dispose node '{NodeName}'", node.Name); }
        }
    }

    public async Task<bool> StopExecution()
    {
        bool hadExecutor;
        Task retirement;
        lock (_executionSync)
        {
            hadExecutor = _executor != null || _startPending;
            RetireExecution();
            retirement = _retirement;
        }
        await retirement;
        return hadExecutor;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_executionSync) _disposed = true;
        await StopExecution();
        if (_engine != null)
        {
            await CleanupGraphAsync(Task.CompletedTask, _engine);
            _engine = null;
        }
        StateChanged = null;
        NodeActivated = null;
        NodeDeactivated = null;
    }

    public ExecutionStatus GetExecutionStatus()
    {
        lock (_executionSync) return new() { State = CurrentState, CurrentNodeName = CurrentNodeName,
            BusinessResult = _context?.BusinessResult, BusinessRun = _context?.BusinessRun };
    }

    private void OnFSMStateChanged(FSMExecutor executor, FSMState newState, FSMState oldState)
    {
        lock (_executionSync)
        {
            if (_disposed || !ReferenceEquals(executor, _executor)) return;
            CurrentState = newState switch
            {
                FSMState.Running or FSMState.Proceeding or FSMState.Pausing => ExecutionState.Running,
                FSMState.Paused => ExecutionState.Paused,
                FSMState.Stopping => ExecutionState.Stopping,
                FSMState.Finished => ExecutionState.Finished,
                _ => ExecutionState.Idle,
            };
            StateChanged?.Invoke(CurrentState, CurrentNodeName);
        }
    }

    private void OnNodeStateChanged(object? sender, string nodeName)
    {
        Action<string>? notify;
        lock (_executionSync)
        {
            if (_disposed || !ReferenceEquals(sender, _executor)) return;
            CurrentNodeName = nodeName;
            notify = NodeActivated;
        }
        // A subscriber may reenter core control. Do not lend it the host lock:
        // another core notification can already be waiting for that lock.
        notify?.Invoke(nodeName);
    }

    private void OnNodeExitChanged(object? sender, string nodeName)
    {
        Action<string>? notify;
        lock (_executionSync)
        {
            if (_disposed || !ReferenceEquals(sender, _executor)) return;
            notify = NodeDeactivated;
        }
        notify?.Invoke(nodeName);
    }

    public event Action<ExecutionState, string?>? StateChanged;
    public event Action<string>? NodeActivated;
    public event Action<string>? NodeDeactivated;
}
