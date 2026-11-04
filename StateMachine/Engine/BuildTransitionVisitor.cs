namespace StateMachine;

public class BuildTransitionVisitor : StateMachineScriptBaseVisitor<object>
{
    public Dictionary<string, FSMEvent> EventDict;
    public Dictionary<string, IFSMNode> NodeDict;
    public Dictionary<string, FsmModuleInstance> ModuleInstances;
    private string nameprev = "";
    private FsmModuleInterfaceResolver Interfaces => new(ModuleInstances, NodeDict, EventDict);

    public BuildTransitionVisitor(Dictionary<string, FSMEvent> eventDict, Dictionary<string, IFSMNode> nodeDict,
        Dictionary<string, FsmModuleInstance> moduleInstances)
        => (EventDict, NodeDict, ModuleInstances) = (eventDict, nodeDict, moduleInstances);

    public override object? VisitNamespace(StateMachineScriptParser.NamespaceContext context)
    {
        var saved = nameprev;
        nameprev = context.STRING().GetText() + ".";
        foreach (var expression in context.expression()) Visit(expression);
        nameprev = saved;
        return null;
    }

    public override object? VisitModule_statement(StateMachineScriptParser.Module_statementContext context) => null;
    public override object? VisitImport_statement(StateMachineScriptParser.Import_statementContext context) => null;
    public override object? VisitModule_instance(StateMachineScriptParser.Module_instanceContext context) => null;

    private string Qualify(string reference)
    {
        var local = nameprev + reference;
        // 优先当前 Namespace，但真实的限定实例名也可以直接引用。
        if (ModuleInstances.ContainsKey(local) || NodeDict.ContainsKey(local)) return local;
        // 完整实例身份优先于不相关实例的词形前缀；普通名称仍沿用 Namespace 前缀规则。
        if (ModuleInstances.ContainsKey(reference) || IsExpandedNode(reference)) return reference;
        if (Interfaces.FindOwner(local) != null) return local;
        return Interfaces.FindOwner(reference) != null ? reference : local;
    }

    private string ResolveTarget(string reference)
    {
        var name = Qualify(reference);
        if (ModuleInstances.ContainsKey(name)) return Interfaces.ResolveInput(name).Name;
        if (NodeDict.ContainsKey(name) && !IsExpandedNode(name)) return name;
        if (Interfaces.FindOwner(name) != null) return Interfaces.ResolveInput(name).Name;
        return name;
    }

    private bool IsExpandedNode(string name)
        => ModuleInstances.Values.Any(instance => instance.ExpandedNodeNames.Contains(name));

    public override object? VisitModuleOutputTransition(StateMachineScriptParser.ModuleOutputTransitionContext context)
    {
        var reference = context.STRING()[0].GetText();
        var local = nameprev + reference;
        var localIsPublic = Interfaces.FindPublicOwner(local, isOutput: true) != null;
        var globalIsPublic = Interfaces.FindPublicOwner(reference, isOutput: true) != null;
        var qualified = localIsPublic || (!globalIsPublic && Interfaces.FindOwner(local) != null) ? local : reference;
        var output = Interfaces.ResolveOutput(qualified);
        var target = ResolveTarget(context.STRING()[1].GetText());
        foreach (var publisher in output.Publishers)
            Connect(NodeDict, publisher.Name, output.Event, target);
        return null;
    }

    public override object? VisitDefTransition(StateMachineScriptParser.DefTransitionContext context)
    {
        var eventName = nameprev + context.STRING()[0].GetText();
        if (ModuleInstances.Values.Any(instance => instance.ExpandedEventNames.Contains(eventName)))
            throw new ScriptException($"事件 {eventName} 与模块限定事件身份碰撞；公开出口只允许实例.Output -> Target;");
        var source = Qualify(context.STRING()[1].GetText());
        if (ModuleInstances.ContainsKey(source))
            throw new ScriptException($"模块实例 {source} 不支持旧事件占位源连线 {eventName}；请使用 {source}.Output -> Target;");
        if (IsExpandedNode(source))
            throw new ScriptException($"模块实例的私有源节点 {source} 不能从外部接线；请使用实例.Output -> Target;");
        var target = ResolveTarget(context.STRING()[2].GetText());
        if (!EventDict.TryGetValue(eventName, out var @event))
            EventDict.Add(eventName, @event = new FSMEvent(eventName));
        Connect(NodeDict, source, @event, target);
        return null;
    }

    internal static void Connect(IReadOnlyDictionary<string, IFSMNode> nodes, string source, FSMEvent @event, string target)
    {
        if (!nodes.TryGetValue(source, out var sourceNode))
            throw new ScriptException($"State {source} 连线出错，未定义该 State");
        if (!nodes.TryGetValue(target, out var targetNode))
            throw new ScriptException($"State {target} 连线出错，未定义该 State");
        if (sourceNode.HasTransition(@event))
            throw new ScriptException($"State {source} 向 {@event.EventName} 连线冲突，已定义从该状态到该事件的转移");
        sourceNode.AddTransition(@event, targetNode);
    }
}
