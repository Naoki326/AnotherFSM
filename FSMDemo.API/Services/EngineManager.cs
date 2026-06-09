using System.Reflection;
using StateMachine;

namespace FSMDemo.API.Services;

public class EngineManager
{
    private FSMEngine? _engine;
    private FSMExecutor? _executor;
    private readonly ILogger<EngineManager> _logger;
    private readonly IFSMNodeFactory _nodeFactory;

    public FSMEngine Engine
    {
        get
        {
            if (_engine == null)
            {
                _engine = FSMEngineBuilder.Create().ConfigureNodeFactory(_nodeFactory).Build();
            }
            return _engine;
        }
    }

    public FSMExecutor? Executor => _executor;
    public ExecutionState CurrentState { get; private set; } = ExecutionState.Idle;
    public string? CurrentNodeName { get; private set; }

    public EngineManager(ILogger<EngineManager> logger, IFSMNodeFactory nodeFactory)
    {
        _logger = logger;
        _nodeFactory = nodeFactory;
    }

    private sealed class ModuleNodeRef
    {
        public string InstanceName { get; set; } = "";
        public FsmModuleInstance Instance { get; set; } = default!;
        public FsmModuleInfo Module { get; set; } = default!;
        public string LocalName { get; set; } = "";
    }

    private readonly record struct SavedConnection(
        string FromNodeName,
        string ToNodeName,
        string EventName,
        string? FromModuleInstanceName = null,
        string? ToModuleInstanceName = null,
        bool UsesOutputEventMap = false);

    public EngineStatus GetStatus()
    {
        var engine = Engine;
        var connectionCount = engine.GetNodeNames().Sum(name =>
            engine[name].GetAllTargets().Count());

        return new EngineStatus
        {
            NodeNames = engine.GetNodeNames().ToList(),
            EventNames = engine.GetEventNames().ToList(),
            ConnectionCount = connectionCount
        };
    }

    public string Export()
    {
        var sb = new System.Text.StringBuilder();
        var engine = Engine;
        var moduleNames = engine.GetModuleNames().ToList();
        var instanceNames = engine.ModuleInstances.Keys.ToList();

        // 1. Module definitions
        foreach (var moduleName in moduleNames)
        {
            if (!engine.TryGetModule(moduleName, out var info)) continue;

            sb.AppendLine($"module {moduleName} {{");

            if (info.Inputs.Count > 0)
                sb.AppendLine($"    input {string.Join(", ", info.Inputs)};");
            if (info.Outputs.Count > 0)
                sb.AppendLine($"    output {string.Join(", ", info.Outputs)};");
            if (info.Terminals.Count > 0)
                sb.AppendLine($"    terminal {string.Join(", ", info.Terminals)};");

            foreach (var kvp in info.TemplateNodes)
            {
                kvp.Value.UpdateEventDescriptions();
                sb.Append(kvp.Value.ToString());
            }

            foreach (var kvp in info.TemplateEvents)
                sb.AppendLine($"    def {kvp.Key} as event;");

            foreach (var trans in info.PendingTransitions)
                sb.AppendLine($"    {trans.EventName}->{trans.SourceNode} to {trans.TargetNode};");

            sb.AppendLine("}");
            sb.AppendLine();
        }

        // 2. Import statements
        foreach (var moduleName in moduleNames)
            sb.AppendLine($"import {moduleName};");

        // 3. Module instance declarations
        foreach (var kvp in engine.ModuleInstances)
        {
            var inst = kvp.Value;
            sb.AppendLine();
            sb.Append($"def {inst.InstanceName}({inst.ModuleName}) {{");
            // Use module definition's Outputs order for correct index mapping
            if (engine.TryGetModule(inst.ModuleName, out var moduleDef))
            {
                foreach (var outputName in moduleDef.Outputs)
                {
                    if (inst.OutputEventMap.TryGetValue(outputName, out var extEvent))
                        sb.Append($"\r\n\t{outputName}->{extEvent};");
                }
            }
            else
            {
                var i = 0;
                foreach (var ev in inst.OutputEventMap)
                {
                    sb.Append($"\r\n\t{i}->{ev.Value};");
                    i++;
                }
            }
            sb.Append($"\r\n\tPos:({inst.PosX}, {inst.PosY});");
            if (!string.IsNullOrEmpty(inst.Color)) sb.Append($"\r\n\tColor: \"{inst.Color}\";");
            sb.AppendLine("\r\n}");
        }

        if (moduleNames.Count > 0 || instanceNames.Count > 0)
            sb.AppendLine();

        // 4. Regular nodes (skip module-internal)
        foreach (var name in engine.GetNodeNames())
        {
            if (instanceNames.Any(p => name.StartsWith(p + "."))) continue;
            var node = engine[name];
            sb.Append(node.ToString());
        }

        // 5. Module instance outgoing transitions (internal -> external)
        foreach (var instName in instanceNames)
        {
            var prefix = instName + ".";
            foreach (var intNodeName in engine.GetNodeNames().Where(n => n.StartsWith(prefix)))
            {
                var intNode = engine[intNodeName];
                foreach (var trans in intNode.GetFSMTransitions())
                {
                    var targetName = trans.Target.Name;
                    if (targetName.StartsWith(prefix)) continue;
                    sb.AppendLine($"{trans.Trigger.EventName}->{instName} to {targetName};");
                }
            }
        }

        return sb.ToString();
    }

    public void Import(string script)
    {
        _engine = null;
        Engine.CreateStateMachine(script);
    }

    private List<GroupDefDto>? GetGroupDefs(IFSMNode node)
    {
        var classType = node.ClassType ?? "";
        var type = node.GetType();

        if (classType.StartsWith("Group", StringComparison.OrdinalIgnoreCase))
        {
            var startNode = type.GetProperty("StartNode")?.GetValue(node)?.ToString() ?? "";
            var endEvent = type.GetProperty("EndEvent")?.GetValue(node)?.ToString() ?? "";
            return [new GroupDefDto { StartNode = startNode, EndEvent = endEvent }];
        }

        if (classType.StartsWith("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            var fsms = type.GetProperty("FSMs")?.GetValue(node) as System.Collections.IList;
            if (fsms != null)
            {
                var result = new List<GroupDefDto>();
                foreach (var fsm in fsms)
                {
                    var fsmType = fsm!.GetType();
                    result.Add(new GroupDefDto
                    {
                        StartNode = fsmType.GetProperty("StartNode")?.GetValue(fsm)?.ToString() ?? "",
                        EndEvent = fsmType.GetProperty("EndEvent")?.GetValue(fsm)?.ToString() ?? "",
                    });
                }
                return result;
            }
        }

        return null;
    }

    private static bool ApplyGroupDefs(IFSMNode node, List<GroupDefDto> groupDefs)
    {
        var type = node.GetType();
        var classType = node.ClassType ?? "";

        if (classType.StartsWith("Group", StringComparison.OrdinalIgnoreCase) && groupDefs.Count == 1)
        {
            var startProp = type.GetProperty("StartNode");
            var endProp = type.GetProperty("EndEvent");
            if (startProp != null && endProp != null)
            {
                startProp.SetValue(node, groupDefs[0].StartNode);
                endProp.SetValue(node, groupDefs[0].EndEvent);
                return true;
            }
        }

        if (classType.StartsWith("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            var fsmsProp = type.GetProperty("FSMs");
            if (fsmsProp != null)
            {
                var fsms = fsmsProp.GetValue(node) as System.Collections.IList;
                fsms?.Clear();
                foreach (var def in groupDefs)
                {
                    var fsmType = fsmsProp.PropertyType.GetGenericArguments().First();
                    var fsmInstance = Activator.CreateInstance(fsmType)!;
                    fsmType.GetProperty("StartNode")?.SetValue(fsmInstance, def.StartNode);
                    fsmType.GetProperty("EndEvent")?.SetValue(fsmInstance, def.EndEvent);
                    fsms?.Add(fsmInstance);
                }
                return true;
            }
        }

        return false;
    }

    private ModuleNodeRef? GetModuleNodeRef(string nodeName)
    {
        foreach (var kvp in Engine.ModuleInstances.OrderByDescending(kvp => kvp.Key.Length))
        {
            var prefix = kvp.Key + ".";
            if (!nodeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Engine.TryGetModule(kvp.Value.ModuleName, out var module)) return null;

            return new ModuleNodeRef
            {
                InstanceName = kvp.Key,
                Instance = kvp.Value,
                Module = module,
                LocalName = nodeName[prefix.Length..],
            };
        }

        return null;
    }

    private bool TryGetSameInstanceInternalNodes(
        string fromNodeName,
        string toNodeName,
        out ModuleNodeRef fromRef,
        out ModuleNodeRef toRef)
    {
        fromRef = default!;
        toRef = default!;

        var source = GetModuleNodeRef(fromNodeName);
        var target = GetModuleNodeRef(toNodeName);
        if (source == null || target == null) return false;
        if (!string.Equals(source.InstanceName, target.InstanceName, StringComparison.OrdinalIgnoreCase)) return false;

        fromRef = source;
        toRef = target;
        return true;
    }

    private static bool IsModuleOutput(FsmModuleInfo module, string eventName)
    {
        return module.Outputs.Any(output => string.Equals(output, eventName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeTemplateNodeName(ModuleNodeRef moduleNode, string name)
    {
        var prefix = moduleNode.InstanceName + ".";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..]
            : name;
    }

    private static string NormalizeTemplateEventName(ModuleNodeRef moduleNode, string eventName)
    {
        foreach (var output in moduleNode.Instance.OutputEventMap)
        {
            if (string.Equals(output.Value, eventName, StringComparison.OrdinalIgnoreCase))
                return output.Key;
        }

        return NormalizeTemplateNodeName(moduleNode, eventName);
    }

    private static List<GroupDefDto> NormalizeTemplateGroupDefs(ModuleNodeRef moduleNode, List<GroupDefDto> groupDefs)
    {
        return groupDefs.Select(def => new GroupDefDto
        {
            StartNode = NormalizeTemplateNodeName(moduleNode, def.StartNode),
            EndEvent = NormalizeTemplateEventName(moduleNode, def.EndEvent),
        }).ToList();
    }

    private bool TryResolveConnectionSourceNodes(string fromNodeName, string eventName, out List<string> sourceNodeNames, out string error)
    {
        sourceNodeNames = [];
        error = "";

        if (Engine.ModuleInstances.TryGetValue(fromNodeName, out var instance))
        {
            var terminalNodeNames = instance.TerminalNodeNames
                .Where(Engine.ContainsNode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (terminalNodeNames.Count > 0)
            {
                sourceNodeNames = terminalNodeNames
                    .Where(nodeName => FSMNodeBranchEventHelper.PublishesEvent(Engine[nodeName], eventName))
                    .ToList();

                if (sourceNodeNames.Count > 0)
                    return true;

                if (terminalNodeNames.Count == 1)
                {
                    sourceNodeNames = terminalNodeNames;
                    return true;
                }

                error = $"Module instance '{fromNodeName}' has multiple terminal nodes, but none publishes event '{eventName}'";
                return false;
            }

            // Backward compatibility for modules that still model outputs as event-to-source mappings.
            if (sourceNodeNames.Count == 0
                && instance.ExternalEventToInternalNodes.TryGetValue(eventName, out var legacyOutputNodes))
            {
                sourceNodeNames = legacyOutputNodes
                    .Where(Engine.ContainsNode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (sourceNodeNames.Count == 0)
            {
                error = $"Module instance '{fromNodeName}' has no terminal node";
                return false;
            }

            return true;
        }

        if (Engine.ContainsNode(fromNodeName))
        {
            sourceNodeNames = [fromNodeName];
            return true;
        }

        error = $"Node or module instance '{fromNodeName}' not found";
        return false;
    }

    private bool TryResolveConnectionTargetNodes(string toNodeName, out List<string> targetNodeNames, out string error)
    {
        targetNodeNames = [];
        error = "";

        if (Engine.ModuleInstances.TryGetValue(toNodeName, out var instance))
        {
            targetNodeNames = instance.InputNodeNames
                .Where(Engine.ContainsNode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targetNodeNames.Count == 0)
            {
                error = $"Module instance '{toNodeName}' has no input node";
                return false;
            }

            return true;
        }

        if (Engine.ContainsNode(toNodeName))
        {
            targetNodeNames = [toNodeName];
            return true;
        }

        error = $"Node or module instance '{toNodeName}' not found";
        return false;
    }

    private bool CanAddTransition(
        string fromNodeName,
        string toNodeName,
        string eventName,
        string? ignoreFromNodeName = null,
        string? ignoreToNodeName = null,
        string? ignoreEventName = null)
    {
        if (!Engine.ContainsNode(fromNodeName) || !Engine.ContainsNode(toNodeName))
            return false;

        var fromNode = Engine[fromNodeName];
        foreach (var transition in fromNode.GetFSMTransitions())
        {
            var isIgnored = ignoreFromNodeName != null
                && ignoreToNodeName != null
                && string.Equals(fromNodeName, ignoreFromNodeName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(transition.Target.Name, ignoreToNodeName, StringComparison.OrdinalIgnoreCase)
                && (ignoreEventName == null
                    || string.Equals(transition.Trigger.EventName, ignoreEventName, StringComparison.OrdinalIgnoreCase));

            if (isIgnored)
                continue;

            if (string.Equals(transition.Trigger.EventName, eventName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static FSMEvent EnsureTemplateEvent(FsmModuleInfo module, string eventName)
    {
        if (!module.TemplateEvents.TryGetValue(eventName, out var fsmEvent))
        {
            fsmEvent = new FSMEvent(eventName);
            module.TemplateEvents[eventName] = fsmEvent;
        }

        return fsmEvent;
    }

    private FSMEvent EnsureEngineEvent(string eventName)
    {
        if (!Engine.TryGetEvent(eventName, out var fsmEvent))
        {
            fsmEvent = new FSMEvent(eventName);
            Engine.AddEvent(fsmEvent);
        }

        return fsmEvent;
    }

    private static string GetExpandedEventName(FsmModuleInfo template, FsmModuleInstance instance, string templateEventName)
    {
        return IsModuleOutput(template, templateEventName)
            && instance.OutputEventMap.TryGetValue(templateEventName, out var mappedEvent)
                ? mappedEvent
                : instance.InstanceName + "." + templateEventName;
    }

    private static string? GetAffectedInstanceName(string nodeName, IEnumerable<string> affectedInstanceNames)
    {
        foreach (var instanceName in affectedInstanceNames.OrderByDescending(name => name.Length))
        {
            if (nodeName.StartsWith(instanceName + ".", StringComparison.OrdinalIgnoreCase))
                return instanceName;
        }

        return null;
    }

    private static bool ContainsName(IEnumerable<string> names, string name)
    {
        return names.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetLegacyOutputSource(FsmModuleInstance instance, string sourceNodeName, string eventName)
    {
        return instance.TerminalNodeNames.Count == 0
            && instance.ExternalEventToInternalNodes.TryGetValue(eventName, out var sourceNodes)
            && ContainsName(sourceNodes, sourceNodeName);
    }

    private List<SavedConnection> CaptureExternalConnections(HashSet<string> affectedInstanceNames)
    {
        var connections = new List<SavedConnection>();
        foreach (var nodeName in Engine.GetNodeNames().ToList())
        {
            if (!Engine.TryGetNode(nodeName, out var node)) continue;
            foreach (var transition in node.GetFSMTransitions().ToList())
            {
                var sourceInstance = GetAffectedInstanceName(transition.Source.Name, affectedInstanceNames);
                var targetInstance = GetAffectedInstanceName(transition.Target.Name, affectedInstanceNames);
                if (sourceInstance == null && targetInstance == null) continue;
                if (sourceInstance != null
                    && targetInstance != null
                    && string.Equals(sourceInstance, targetInstance, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? fromModuleInstanceName = null;
                string? toModuleInstanceName = null;
                var usesOutputEventMap = false;

                if (sourceInstance != null
                    && Engine.ModuleInstances.TryGetValue(sourceInstance, out var sourceModuleInstance))
                {
                    if (ContainsName(sourceModuleInstance.TerminalNodeNames, transition.Source.Name))
                    {
                        fromModuleInstanceName = sourceInstance;
                    }
                    else if (TryGetLegacyOutputSource(sourceModuleInstance, transition.Source.Name, transition.Trigger.EventName))
                    {
                        fromModuleInstanceName = sourceInstance;
                        usesOutputEventMap = true;
                    }
                }

                if (targetInstance != null
                    && Engine.ModuleInstances.TryGetValue(targetInstance, out var targetModuleInstance)
                    && ContainsName(targetModuleInstance.InputNodeNames, transition.Target.Name))
                {
                    toModuleInstanceName = targetInstance;
                }

                connections.Add(new SavedConnection(
                    transition.Source.Name,
                    transition.Target.Name,
                    transition.Trigger.EventName,
                    fromModuleInstanceName,
                    toModuleInstanceName,
                    usesOutputEventMap));
            }
        }

        return connections.Distinct().ToList();
    }

    private void RemoveExpandedNodes(HashSet<string> affectedInstanceNames)
    {
        var internalNodeNames = Engine.GetNodeNames()
            .Where(name => GetAffectedInstanceName(name, affectedInstanceNames) != null)
            .ToList();

        foreach (var nodeName in Engine.GetNodeNames().ToList())
        {
            if (!Engine.TryGetNode(nodeName, out var node)) continue;
            foreach (var internalNodeName in internalNodeNames)
            {
                if (Engine.TryGetNode(internalNodeName, out var internalNode))
                    node.DeleteTransition(internalNode);
            }
        }

        foreach (var nodeName in internalNodeNames)
        {
            if (Engine.TryGetNode(nodeName, out var node))
                node.ClearTransition();
            Engine.TryDeleteNode(nodeName);
        }

        foreach (var instanceName in affectedInstanceNames)
        {
            if (Engine.ModuleInstances.TryGetValue(instanceName, out var instance))
            {
                instance.ExternalEventToInternalNodes.Clear();
                instance.InputNodeNames.Clear();
                instance.TerminalNodeNames.Clear();
            }
        }
    }

    private static string OutputEventRenameKey(string instanceName, string eventName)
    {
        return instanceName + "\u001f" + eventName;
    }

    private void RestoreExternalConnections(
        IEnumerable<SavedConnection> connections,
        IReadOnlyDictionary<string, string>? renamedOutputEvents = null)
    {
        foreach (var connection in connections)
        {
            var eventName = connection.EventName;
            if (connection.UsesOutputEventMap
                && connection.FromModuleInstanceName != null
                && renamedOutputEvents != null
                && renamedOutputEvents.TryGetValue(OutputEventRenameKey(connection.FromModuleInstanceName, connection.EventName), out var renamedEventName))
            {
                eventName = renamedEventName;
            }

            List<string> sourceNodeNames;
            if (connection.FromModuleInstanceName != null)
            {
                if (!TryResolveConnectionSourceNodes(connection.FromModuleInstanceName, eventName, out sourceNodeNames, out _))
                    continue;
            }
            else
            {
                if (!Engine.ContainsNode(connection.FromNodeName))
                    continue;
                sourceNodeNames = [connection.FromNodeName];
            }

            List<string> targetNodeNames;
            if (connection.ToModuleInstanceName != null)
            {
                if (!TryResolveConnectionTargetNodes(connection.ToModuleInstanceName, out targetNodeNames, out _))
                    continue;
            }
            else
            {
                if (!Engine.ContainsNode(connection.ToNodeName))
                    continue;
                targetNodeNames = [connection.ToNodeName];
            }

            foreach (var sourceNodeName in sourceNodeNames)
            {
                foreach (var targetNodeName in targetNodeNames)
                {
                    RestoreConnection(sourceNodeName, targetNodeName, eventName, connection.FromModuleInstanceName != null && !connection.UsesOutputEventMap);
                }
            }
        }
    }

    private void RestoreConnection(string fromNodeName, string toNodeName, string eventName, bool ensureSourcePublishesEvent = false)
    {
        if (!Engine.ContainsNode(fromNodeName) || !Engine.ContainsNode(toNodeName))
            return;

        var fsmEvent = EnsureEngineEvent(eventName);
        var fromNode = Engine[fromNodeName];
        var toNode = Engine[toNodeName];
        if (ensureSourcePublishesEvent && !FSMNodeBranchEventHelper.EnsurePublishesEvent(fromNode, fsmEvent))
            return;

        if (fromNode.HasTransition(fsmEvent))
            fromNode.DeleteTransition(fsmEvent);

        fromNode.AddTransition(fsmEvent, toNode);
    }

    private bool AddConnectionExact(string fromNodeName, string toNodeName, string eventName, bool ensureSourcePublishesEvent = false)
    {
        var fsmEvent = EnsureEngineEvent(eventName);
        if (ensureSourcePublishesEvent && !FSMNodeBranchEventHelper.EnsurePublishesEvent(Engine[fromNodeName], fsmEvent))
            return false;

        Engine[fromNodeName].AddTransition(fsmEvent, Engine[toNodeName]);
        return true;
    }

    private bool DeleteConnectionExact(string fromNodeName, string toNodeName, string eventName)
    {
        if (!Engine.TryGetEvent(eventName, out var fsmEvent))
            return false;

        var fromNode = Engine[fromNodeName];
        if (!fromNode.HasTransition(fsmEvent))
            return false;

        var transition = fromNode.GetFSMTransitions()
            .FirstOrDefault(t =>
                string.Equals(t.Trigger.EventName, eventName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Target.Name, toNodeName, StringComparison.OrdinalIgnoreCase));
        if (transition == null)
            return false;

        return fromNode.DeleteTransition(fsmEvent);
    }

    private bool IsTerminalSourceForVirtualModule(string moduleInstanceName, string sourceNodeName)
    {
        return Engine.ModuleInstances.TryGetValue(moduleInstanceName, out var instance)
            && instance.TerminalNodeNames.Any(nodeName =>
                string.Equals(nodeName, sourceNodeName, StringComparison.OrdinalIgnoreCase));
    }

    private static string TerminalPublishError(string moduleInstanceName, string sourceNodeName, string eventName)
    {
        return $"Module instance '{moduleInstanceName}' terminal node '{sourceNodeName}' has multiple event branches; set the terminal branch event to '{eventName}' explicitly";
    }

    private void ExpandModuleInstanceNodes(FsmModuleInfo template, FsmModuleInstance instance)
    {
        var prefix = instance.InstanceName + ".";
        instance.ExternalEventToInternalNodes.Clear();
        instance.InputNodeNames.Clear();
        instance.TerminalNodeNames.Clear();

        foreach (var kvp in template.TemplateNodes)
            kvp.Value.UpdateEventDescriptions();

        foreach (var kvp in template.TemplateNodes)
        {
            var templateNodeName = kvp.Key;
            var templateNode = kvp.Value;
            var newNodeName = prefix + templateNodeName;

            Engine.CreateNode(templateNode.ClassType, newNodeName);
            var newNode = Engine[(ScriptNode)newNodeName];

            newNode.PosX = templateNode.PosX;
            newNode.PosY = templateNode.PosY;
            newNode.Color = templateNode.Color;
            newNode.FlowID = Guid.NewGuid().ToString();

            CopyGroupInfo(templateNode, newNode, prefix);

            foreach (var desc in templateNode.EventDescriptions)
            {
                var finalEventName = GetExpandedEventName(template, instance, desc.Description);
                EnsureEngineEvent(finalEventName);

                newNode.SetBranchEvent(desc.Index, Engine[(ScriptEvent)finalEventName]);

                if (instance.OutputEventMap.TryGetValue(desc.Description, out var mappedEvent))
                {
                    if (!instance.ExternalEventToInternalNodes.ContainsKey(mappedEvent))
                        instance.ExternalEventToInternalNodes[mappedEvent] = [];
                    if (!instance.ExternalEventToInternalNodes[mappedEvent].Contains(newNodeName))
                        instance.ExternalEventToInternalNodes[mappedEvent].Add(newNodeName);
                }
            }

            if (template.Inputs.Contains(templateNodeName, StringComparer.OrdinalIgnoreCase))
                instance.InputNodeNames.Add(newNodeName);
            if (template.Terminals.Contains(templateNodeName, StringComparer.OrdinalIgnoreCase))
                instance.TerminalNodeNames.Add(newNodeName);
        }

        foreach (var kvp in template.TemplateEvents)
        {
            var finalEventName = GetExpandedEventName(template, instance, kvp.Key);
            EnsureEngineEvent(finalEventName);
        }

        foreach (var trans in template.PendingTransitions)
        {
            var sourceNodeName = prefix + trans.SourceNode;
            var targetNodeName = prefix + trans.TargetNode;
            var eventName = GetExpandedEventName(template, instance, trans.EventName);

            EnsureEngineEvent(eventName);
            Engine.ForceConnectNode(eventName, sourceNodeName, targetNodeName);
        }
    }

    private void RebuildModuleInstances(string moduleName)
    {
        if (!Engine.TryGetModule(moduleName, out var template)) return;

        var instances = Engine.ModuleInstances.Values
            .Where(instance => string.Equals(instance.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (instances.Count == 0) return;

        var affectedInstanceNames = instances
            .Select(instance => instance.InstanceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalConnections = CaptureExternalConnections(affectedInstanceNames);

        RemoveExpandedNodes(affectedInstanceNames);
        foreach (var instance in instances)
            ExpandModuleInstanceNodes(template, instance);

        RestoreExternalConnections(externalConnections);
    }

    private bool UpdateModuleInstanceOutputEvent(FsmModuleInstance instance, int index, string newEventName)
    {
        if (!Engine.TryGetModule(instance.ModuleName, out var module)) return false;
        if (index < 0 || index >= module.Outputs.Count) return false;

        var outputName = module.Outputs[index];
        instance.OutputEventMap.TryGetValue(outputName, out var oldEventName);
        if (string.Equals(oldEventName, newEventName, StringComparison.OrdinalIgnoreCase))
            return true;

        var affectedInstanceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            instance.InstanceName,
        };
        var externalConnections = CaptureExternalConnections(affectedInstanceNames);
        var renamedOutputEvents = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(oldEventName))
            renamedOutputEvents[OutputEventRenameKey(instance.InstanceName, oldEventName)] = newEventName;

        instance.OutputEventMap[outputName] = newEventName;
        RemoveExpandedNodes(affectedInstanceNames);
        ExpandModuleInstanceNodes(module, instance);
        RestoreExternalConnections(externalConnections, renamedOutputEvents);

        return true;
    }

    private ModuleInstanceDto BuildModuleInstanceDto(FsmModuleInstance instance)
    {
        var prefix = instance.InstanceName + ".";
        var internalNodes = Engine.GetNodeNames()
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var outputSourceNodes = new Dictionary<string, string>();
        foreach (var output in instance.OutputEventMap)
        {
            if (instance.ExternalEventToInternalNodes.TryGetValue(output.Value, out var nodes) && nodes.Count > 0)
                outputSourceNodes[output.Key] = nodes[0];
        }

        return new ModuleInstanceDto
        {
            InstanceName = instance.InstanceName,
            ModuleName = instance.ModuleName,
            PosX = instance.PosX,
            PosY = instance.PosY,
            Color = instance.Color,
            FlowID = instance.FlowID,
            InternalNodeNames = internalNodes,
            InputNodeNames = instance.InputNodeNames,
            TerminalNodeNames = instance.TerminalNodeNames,
            OutputEventMap = instance.OutputEventMap,
            OutputSourceNodes = outputSourceNodes,
        };
    }

    public List<NodeInfo> GetNodes()
    {
        return Engine.GetNodeNames().Select(name =>
        {
            var node = Engine[name];
            node.UpdateEventDescriptions();
            return new NodeInfo
            {
                Name = node.Name,
                ClassType = node.ClassType ?? "",
                Color = node.Color ?? "",
                PosX = node.PosX,
                PosY = node.PosY,
                FlowID = node.FlowID ?? "",
                Discription = node.Discription,
                EventDescriptions = node.EventDescriptions?
                    .Select(ed => new NodeEventDescriptionDto { Index = ed.Index, Description = ed.Description })
                    .ToList() ?? [],
                GroupDefs = GetGroupDefs(node),
            };
        }).ToList();
    }

    public NodeInfo? GetNode(string name)
    {
        if (!Engine.TryGetNode(name, out var node)) return null;
        node.UpdateEventDescriptions();
        return new NodeInfo
        {
            Name = node.Name,
            ClassType = node.ClassType ?? "",
            Color = node.Color ?? "",
            PosX = node.PosX,
            PosY = node.PosY,
            FlowID = node.FlowID ?? "",
            Discription = node.Discription,
            EventDescriptions = node.EventDescriptions?
                .Select(ed => new NodeEventDescriptionDto { Index = ed.Index, Description = ed.Description })
                .ToList() ?? [],
            GroupDefs = GetGroupDefs(node),
        };
    }

    public NodeInfo CreateNode(string type, string name, double posX, double posY, string color = "")
    {
        var actualName = name;
        var suffix = 1;
        while (Engine.ContainsNode(actualName))
        {
            actualName = $"{name}{suffix}";
            suffix++;
        }

        Engine.CreateNode(type, actualName);
        var node = Engine[actualName];
        node.PosX = posX;
        node.PosY = posY;
        node.ClassType = type;
        if (!string.IsNullOrEmpty(color))
            node.Color = color;

        if (node.GetType().GetCustomAttributes(typeof(FSMNodeAttribute), true).FirstOrDefault() is FSMNodeAttribute info
            && info.Indexes.Length == info.EventDescriptions.Length)
        {
            node.EventDescriptions = Enumerable.Range(0, info.Indexes.Length)
                .Select(i => new NodeEventDescription { Index = info.Indexes[i], Description = info.EventDescriptions[i] })
                .ToList();
            foreach (var ed in node.EventDescriptions)
            {
                if (!Engine.TryGetEvent(ed.Description, out FSMEvent e))
                {
                    e = new FSMEvent(ed.Description);
                    Engine.AddEvent(e);
                }
                node.SetBranchEvent(ed.Index, e);
            }
        }

        return GetNode(actualName)!;
    }

    public bool DeleteNode(string name)
    {
        if (!Engine.ContainsNode(name)) return false;
        Engine[name].ClearTransition();
        Engine.TryDeleteNode(name);
        return true;
    }

    public bool RenameNode(string oldName, string newName)
    {
        return Engine.TryChangeNodeName(oldName, newName);
    }

    public bool UpdateNodePosition(string name, double posX, double posY)
    {
        var moduleNode = GetModuleNodeRef(name);
        if (moduleNode != null)
        {
            if (!moduleNode.Module.TemplateNodes.TryGetValue(moduleNode.LocalName, out var templateNode))
                return false;

            templateNode.PosX = posX;
            templateNode.PosY = posY;
            RebuildModuleInstances(moduleNode.Instance.ModuleName);
            return true;
        }

        if (!Engine.TryGetNode(name, out var node)) return false;
        node.PosX = posX;
        node.PosY = posY;
        return true;
    }

    public bool UpdateNodeEvent(string name, int index, string newEventName)
    {
        if (Engine.ModuleInstances.TryGetValue(name, out var moduleInstance))
            return UpdateModuleInstanceOutputEvent(moduleInstance, index, newEventName);

        var moduleNode = GetModuleNodeRef(name);
        if (moduleNode != null)
        {
            if (!moduleNode.Module.TemplateNodes.TryGetValue(moduleNode.LocalName, out var templateNode))
                return false;

            var templateEventName = NormalizeTemplateEventName(moduleNode, newEventName);
            var templateEvent = EnsureTemplateEvent(moduleNode.Module, templateEventName);
            templateNode.SetBranchEvent(index, templateEvent);
            templateNode.UpdateEventDescriptions();
            RebuildModuleInstances(moduleNode.Instance.ModuleName);
            return true;
        }

        if (!Engine.TryGetNode(name, out var node)) return false;

        if (!Engine.TryGetEvent(newEventName, out FSMEvent? sEvent))
        {
            sEvent = new FSMEvent(newEventName);
            Engine.AddEvent(sEvent);
        }

        node.SetBranchEvent(index, sEvent);

        // Update the event description
        if (node.EventDescriptions != null)
        {
            var ed = node.EventDescriptions.FirstOrDefault(e => e.Index == index);
            if (ed != null) ed.Description = newEventName;
        }

        return true;
    }

    public bool UpdateGroupDefs(string name, List<GroupDefDto> groupDefs)
    {
        var moduleNode = GetModuleNodeRef(name);
        if (moduleNode != null)
        {
            if (!moduleNode.Module.TemplateNodes.TryGetValue(moduleNode.LocalName, out var templateNode))
                return false;

            if (!ApplyGroupDefs(templateNode, NormalizeTemplateGroupDefs(moduleNode, groupDefs)))
                return false;

            RebuildModuleInstances(moduleNode.Instance.ModuleName);
            return true;
        }

        if (!Engine.TryGetNode(name, out var node)) return false;
        return ApplyGroupDefs(node, groupDefs);
    }

    public bool UpdateModuleTerminal(string name, bool isTerminal)
    {
        var moduleNode = GetModuleNodeRef(name);
        if (moduleNode == null)
            return false;
        if (!moduleNode.Module.TemplateNodes.ContainsKey(moduleNode.LocalName))
            return false;

        var existing = moduleNode.Module.Terminals.FindIndex(terminal =>
            string.Equals(terminal, moduleNode.LocalName, StringComparison.OrdinalIgnoreCase));

        if (isTerminal)
        {
            if (existing < 0)
                moduleNode.Module.Terminals.Add(moduleNode.LocalName);
        }
        else if (existing >= 0)
        {
            moduleNode.Module.Terminals.RemoveAt(existing);
        }

        RebuildModuleInstances(moduleNode.Instance.ModuleName);
        return true;
    }

    public List<ConnectionDto> GetConnections()
    {
        var connections = new List<ConnectionDto>();
        foreach (var name in Engine.GetNodeNames())
        {
            var node = Engine[name];
            foreach (var transition in node.GetFSMTransitions())
            {
                connections.Add(new ConnectionDto
                {
                    FromNodeName = transition.Source.Name,
                    ToNodeName = transition.Target.Name,
                    EventName = transition.Trigger.EventName
                });
            }
        }
        return connections;
    }

    public (bool Success, string Error) CreateConnection(string fromNodeName, string toNodeName, string eventName)
    {
        if (TryGetSameInstanceInternalNodes(fromNodeName, toNodeName, out var fromModuleNode, out var toModuleNode))
        {
            if (!fromModuleNode.Module.TemplateNodes.ContainsKey(fromModuleNode.LocalName))
                return (false, $"Module node '{fromModuleNode.LocalName}' not found");
            if (!fromModuleNode.Module.TemplateNodes.ContainsKey(toModuleNode.LocalName))
                return (false, $"Module node '{toModuleNode.LocalName}' not found");

            if (fromModuleNode.Module.PendingTransitions.Any(trans =>
                string.Equals(trans.SourceNode, fromModuleNode.LocalName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(trans.TargetNode, toModuleNode.LocalName, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, $"Connection from '{fromNodeName}' to '{toNodeName}' already exists");
            }

            var templateEventName = NormalizeTemplateEventName(fromModuleNode, eventName);
            EnsureTemplateEvent(fromModuleNode.Module, templateEventName);
            fromModuleNode.Module.PendingTransitions.Add(new TemplateTransition
            {
                SourceNode = fromModuleNode.LocalName,
                EventName = templateEventName,
                TargetNode = toModuleNode.LocalName,
            });

            RebuildModuleInstances(fromModuleNode.Instance.ModuleName);
            return (true, "");
        }

        if (!TryResolveConnectionSourceNodes(fromNodeName, eventName, out var sourceNodeNames, out var sourceError))
            return (false, sourceError);
        if (!TryResolveConnectionTargetNodes(toNodeName, out var targetNodeNames, out var targetError))
            return (false, targetError);

        foreach (var sourceNodeName in sourceNodeNames)
        {
            foreach (var targetNodeName in targetNodeNames)
            {
                if (!CanAddTransition(sourceNodeName, targetNodeName, eventName))
                {
                    return (false, $"Connection from '{sourceNodeName}' using event '{eventName}' already exists");
                }

                if (IsTerminalSourceForVirtualModule(fromNodeName, sourceNodeName))
                {
                    var fsmEvent = EnsureEngineEvent(eventName);
                    if (!FSMNodeBranchEventHelper.EnsurePublishesEvent(Engine[sourceNodeName], fsmEvent))
                        return (false, TerminalPublishError(fromNodeName, sourceNodeName, eventName));
                }
            }
        }

        foreach (var sourceNodeName in sourceNodeNames)
        {
            foreach (var targetNodeName in targetNodeNames)
            {
                AddConnectionExact(
                    sourceNodeName,
                    targetNodeName,
                    eventName,
                    IsTerminalSourceForVirtualModule(fromNodeName, sourceNodeName));
            }
        }

        return (true, "");
    }

    public bool DeleteConnection(string fromNodeName, string toNodeName, string? eventName = null)
    {
        if (TryGetSameInstanceInternalNodes(fromNodeName, toNodeName, out var fromModuleNode, out var toModuleNode))
        {
            var templateEventName = string.IsNullOrWhiteSpace(eventName)
                ? null
                : NormalizeTemplateEventName(fromModuleNode, eventName);
            var removed = fromModuleNode.Module.PendingTransitions.RemoveAll(trans =>
                string.Equals(trans.SourceNode, fromModuleNode.LocalName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(trans.TargetNode, toModuleNode.LocalName, StringComparison.OrdinalIgnoreCase)
                && (templateEventName == null
                    || string.Equals(trans.EventName, templateEventName, StringComparison.OrdinalIgnoreCase)));
            if (removed == 0) return false;

            RebuildModuleInstances(fromModuleNode.Instance.ModuleName);
            return true;
        }

        var resolveEventName = eventName ?? "";
        if (!TryResolveConnectionSourceNodes(fromNodeName, resolveEventName, out var sourceNodeNames, out _))
            return false;
        if (!TryResolveConnectionTargetNodes(toNodeName, out var targetNodeNames, out _))
            return false;

        var removedAny = false;
        foreach (var sourceNodeName in sourceNodeNames)
        {
            var fromNode = Engine[sourceNodeName];
            foreach (var targetNodeName in targetNodeNames)
            {
                if (!string.IsNullOrWhiteSpace(eventName))
                {
                    removedAny |= DeleteConnectionExact(sourceNodeName, targetNodeName, eventName);
                }
                else
                {
                    var targetNode = Engine[targetNodeName];
                    if (!fromNode.HasTransition(targetNode)) continue;
                    fromNode.DeleteTransition(targetNode);
                    removedAny = true;
                }
            }
        }

        return removedAny;
    }

    public bool RenameConnection(string fromNodeName, string toNodeName, string newEventName, string? eventName = null)
    {
        if (TryGetSameInstanceInternalNodes(fromNodeName, toNodeName, out var fromModuleNode, out var toModuleNode))
        {
            var oldTemplateEventName = string.IsNullOrWhiteSpace(eventName)
                ? null
                : NormalizeTemplateEventName(fromModuleNode, eventName);
            var transition = fromModuleNode.Module.PendingTransitions.FirstOrDefault(trans =>
                string.Equals(trans.SourceNode, fromModuleNode.LocalName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(trans.TargetNode, toModuleNode.LocalName, StringComparison.OrdinalIgnoreCase)
                && (oldTemplateEventName == null
                    || string.Equals(trans.EventName, oldTemplateEventName, StringComparison.OrdinalIgnoreCase)));
            if (transition == null) return false;

            var templateEventName = NormalizeTemplateEventName(fromModuleNode, newEventName);
            EnsureTemplateEvent(fromModuleNode.Module, templateEventName);
            transition.EventName = templateEventName;

            RebuildModuleInstances(fromModuleNode.Instance.ModuleName);
            return true;
        }

        var resolveEventName = eventName ?? newEventName;
        if (!TryResolveConnectionSourceNodes(fromNodeName, resolveEventName, out var sourceNodeNames, out _))
            return false;
        if (!TryResolveConnectionTargetNodes(toNodeName, out var targetNodeNames, out _))
            return false;

        var existingTransitions = new List<(string SourceNodeName, string TargetNodeName, string EventName)>();
        foreach (var sourceNodeName in sourceNodeNames)
        {
            var sourceNode = Engine[sourceNodeName];
            foreach (var targetNodeName in targetNodeNames)
            {
                foreach (var transition in sourceNode.GetFSMTransitions())
                {
                    if (!string.Equals(transition.Target.Name, targetNodeName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(eventName)
                        && !string.Equals(transition.Trigger.EventName, eventName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    existingTransitions.Add((sourceNodeName, targetNodeName, transition.Trigger.EventName));
                }
            }
        }

        if (existingTransitions.Count == 0)
            return false;

        foreach (var (sourceNodeName, targetNodeName, oldEventName) in existingTransitions)
        {
            if (!CanAddTransition(sourceNodeName, targetNodeName, newEventName, sourceNodeName, targetNodeName, oldEventName))
                return false;

            if (IsTerminalSourceForVirtualModule(fromNodeName, sourceNodeName))
            {
                var fsmEvent = EnsureEngineEvent(newEventName);
                if (!FSMNodeBranchEventHelper.EnsurePublishesEvent(Engine[sourceNodeName], fsmEvent))
                    return false;
            }
        }

        foreach (var (sourceNodeName, targetNodeName, oldEventName) in existingTransitions)
        {
            DeleteConnectionExact(sourceNodeName, targetNodeName, oldEventName);
        }

        foreach (var (sourceNodeName, targetNodeName, _) in existingTransitions)
        {
            AddConnectionExact(
                sourceNodeName,
                targetNodeName,
                newEventName,
                IsTerminalSourceForVirtualModule(fromNodeName, sourceNodeName));
        }

        return true;
    }

    public async Task<bool> StartExecution(string startNodeName, string endEventName)
    {
        if (_executor != null)
        {
            await StopExecution();
        }

        if (!Engine.TryGetNode(startNodeName, out var startNode))
        {
            _logger.LogWarning("Start node '{NodeName}' not found", startNodeName);
            return false;
        }

        if (!Engine.TryGetEvent(endEventName, out var endEvent))
        {
            endEvent = new FSMEvent(endEventName);
        }

        Engine.ReinitGroupNode();

        _executor = new FSMExecutor(startNode, endEvent);
        _executor.FSMStateChanged += OnFSMStateChanged;
        _executor.NodeStateChanged += OnNodeStateChanged;
        _executor.NodeExitChanged += OnNodeExitChanged;

        await _executor.RestartAsync(true);
        return true;
    }

    public async Task<bool> PauseExecution()
    {
        if (_executor == null) return false;
        return await _executor.PauseAsync();
    }

    public bool ContinueExecution()
    {
        if (_executor == null) return false;
        _executor.Continue();
        return true;
    }

    public async Task<bool> StopExecution()
    {
        if (_executor == null) return false;
        await _executor.StopAsync();
        _executor.FSMStateChanged -= OnFSMStateChanged;
        _executor.NodeStateChanged -= OnNodeStateChanged;
        _executor.NodeExitChanged -= OnNodeExitChanged;
        _executor = null;
        CurrentState = ExecutionState.Idle;
        CurrentNodeName = null;
        return true;
    }

    public ExecutionStatus GetExecutionStatus()
    {
        return new ExecutionStatus
        {
            State = CurrentState,
            CurrentNodeName = CurrentNodeName
        };
    }

    private void OnFSMStateChanged(FSMExecutor executor, FSMState newState, FSMState oldState)
    {
        CurrentState = newState switch
        {
            FSMState.Running or FSMState.Proceeding => ExecutionState.Running,
            FSMState.Pausing => ExecutionState.Running,
            FSMState.Paused => ExecutionState.Paused,
            FSMState.Stopping => ExecutionState.Stopping,
            FSMState.Finished => ExecutionState.Finished,
            FSMState.Stoped => ExecutionState.Idle,
            FSMState.Initialized => ExecutionState.Idle,
            _ => ExecutionState.Idle
        };
        StateChanged?.Invoke(CurrentState, CurrentNodeName);
    }

    private void OnNodeStateChanged(object? sender, string nodeName)
    {
        CurrentNodeName = nodeName;
        NodeActivated?.Invoke(nodeName);
    }

    private void OnNodeExitChanged(object? sender, string nodeName)
    {
        NodeDeactivated?.Invoke(nodeName);
    }

    public List<ModuleInstanceDto> GetModuleInstances()
    {
        return Engine.ModuleInstances.Values
            .Select(BuildModuleInstanceDto)
            .ToList();
    }

    public List<ModuleDefDto> GetModuleDefinitions()
    {
        var result = new List<ModuleDefDto>();
        foreach (var name in Engine.GetModuleNames())
        {
            if (!Engine.TryGetModule(name, out var info)) continue;
            result.Add(new ModuleDefDto
            {
                ModuleName = name,
                Inputs = info.Inputs,
                Outputs = info.Outputs,
                Terminals = info.Terminals,
            });
        }
        return result;
    }

    public ModuleInstanceDto AddModuleInstance(string moduleName, string instanceName, double posX, double posY)
    {
        if (!Engine.TryGetModule(moduleName, out var template))
            throw new InvalidOperationException($"Module '{moduleName}' not found");

        if (Engine.ModuleInstances.ContainsKey(instanceName))
            throw new InvalidOperationException($"Module instance '{instanceName}' already exists");

        // Ensure template content is parsed (module must be fully loaded before adding instances)
        if (template.TemplateNodes.Count == 0 && !string.IsNullOrEmpty(template.ScriptContent))
            throw new InvalidOperationException($"Module '{moduleName}' template is not yet parsed");

        var instance = new FsmModuleInstance
        {
            ModuleName = moduleName,
            InstanceName = instanceName,
            PosX = posX,
            PosY = posY,
            Color = "deep-purple",
        };

        // Auto-generate output event mapping: {InstanceName}_{OutputName}
        foreach (var output in template.Outputs)
        {
            var extEventName = $"{instanceName}_{output}";
            instance.OutputEventMap[output] = extEventName;
        }

        ExpandModuleInstanceNodes(template, instance);
        Engine.AddModuleInstance(instanceName, instance);
        return BuildModuleInstanceDto(instance);
    }

    public bool UpdateModuleInstancePosition(string instanceName, double posX, double posY)
    {
        if (!Engine.ModuleInstances.TryGetValue(instanceName, out var inst)) return false;
        inst.PosX = posX;
        inst.PosY = posY;
        return true;
    }

    public bool DeleteModuleInstance(string instanceName)
    {
        if (!Engine.ModuleInstances.TryGetValue(instanceName, out var inst)) return false;

        var prefix = instanceName + ".";
        var internalNodeNames = Engine.GetNodeNames().Where(n => n.StartsWith(prefix)).ToList();

        // Remove incoming transitions from external nodes to internal nodes
        foreach (var extNodeName in Engine.GetNodeNames().Where(n => !n.StartsWith(prefix)))
        {
            var extNode = Engine[extNodeName];
            foreach (var internalNodeName in internalNodeNames)
            {
                if (Engine.TryGetNode(internalNodeName, out var internalNode))
                    extNode.DeleteTransition(internalNode);
            }
        }

        // Clear transitions on internal nodes, then remove them
        foreach (var nodeName in internalNodeNames)
        {
            if (Engine.TryGetNode(nodeName, out var node))
                node.ClearTransition();
            Engine.TryDeleteNode(nodeName);
        }

        Engine.RemoveModuleInstance(instanceName);
        return true;
    }

    private static void CopyGroupInfo(IFSMNode templateNode, IFSMNode newNode, string prefix)
    {
        // Check if it's a GroupNode
        var tnType = templateNode.GetType();
        var nnType = newNode.GetType();
        var classType = tnType.Name;
        if (classType.StartsWith("Group", StringComparison.OrdinalIgnoreCase))
        {
            var startNode = tnType.GetProperty("StartNode")?.GetValue(templateNode)?.ToString() ?? "";
            var endEvent = tnType.GetProperty("EndEvent")?.GetValue(templateNode)?.ToString() ?? "";
            if (!string.IsNullOrEmpty(startNode))
                nnType.GetProperty("StartNode")?.SetValue(newNode, prefix + startNode);
            if (!string.IsNullOrEmpty(endEvent))
                nnType.GetProperty("EndEvent")?.SetValue(newNode, prefix + endEvent);
        }
        if (classType.StartsWith("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            var fsms = tnType.GetProperty("FSMs")?.GetValue(templateNode) as System.Collections.IList;
            var newFsms = nnType.GetProperty("FSMs")?.GetValue(newNode) as System.Collections.IList;
            if (fsms != null && newFsms != null)
            {
                var fsmDescribeType = fsms.GetType().GetGenericArguments()[0];
                foreach (var fsm in fsms)
                {
                    var newFsm = Activator.CreateInstance(fsmDescribeType);
                    var sn = fsmDescribeType.GetProperty("StartNode")?.GetValue(fsm)?.ToString() ?? "";
                    var ee = fsmDescribeType.GetProperty("EndEvent")?.GetValue(fsm)?.ToString() ?? "";
                    fsmDescribeType.GetProperty("StartNode")?.SetValue(newFsm, string.IsNullOrEmpty(sn) ? sn : prefix + sn);
                    fsmDescribeType.GetProperty("EndEvent")?.SetValue(newFsm, string.IsNullOrEmpty(ee) ? ee : prefix + ee);
                    newFsms.Add(newFsm);
                }
            }
        }
    }

    public event Action<ExecutionState, string?>? StateChanged;
    public event Action<string>? NodeActivated;
    public event Action<string>? NodeDeactivated;
}
