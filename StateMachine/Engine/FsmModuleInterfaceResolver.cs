namespace StateMachine;

/// <summary>公开出口的实际事件与发布节点。发布节点依据当前 Branch 确定，不使用 terminal 或事件映射回退。</summary>
public sealed class FsmModuleOutput
{
    public FSMEvent Event { get; }
    public IReadOnlyList<IFSMNode> Publishers { get; }

    internal FsmModuleOutput(FSMEvent @event, IReadOnlyList<IFSMNode> publishers)
        => (Event, Publishers) = (@event, publishers);
}

/// <summary>模块公开接口的权威解析入口，供脚本构建及图编辑等消费者共用。</summary>
public sealed class FsmModuleInterfaceResolver
{
    private readonly IReadOnlyDictionary<string, FsmModuleInstance> instances;
    private readonly IReadOnlyDictionary<string, IFSMNode> nodes;
    private readonly IReadOnlyDictionary<string, FSMEvent> events;

    internal FsmModuleInterfaceResolver(IReadOnlyDictionary<string, FsmModuleInstance> instances,
        IReadOnlyDictionary<string, IFSMNode> nodes, IReadOnlyDictionary<string, FSMEvent> events)
        => (this.instances, this.nodes, this.events) = (instances, nodes, events);

    internal FsmModuleInstance? FindOwner(string reference)
        => instances.Values.Where(i => reference.StartsWith(i.InstanceName + ".", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.InstanceName.Length).FirstOrDefault();

    internal FsmModuleInstance? FindPublicOwner(string reference, bool isOutput)
    {
        var matches = instances.Values.Where(instance =>
            AnotherFSM.Syntax.DeclarationRules.IsQualifiedInterface(reference, instance.InstanceName,
                (isOutput ? instance.PublicOutputs : instance.PublicInputs).Keys)).ToArray();
        if (matches.Length > 1)
            throw new ScriptException($"模块公开接口 {reference} 有多个有效归属，名称碰撞");
        return matches.SingleOrDefault();
    }

    public IFSMNode ResolveInput(string reference)
    {
        FsmModuleInstance? instance;
        string nodeName;
        if (instances.TryGetValue(reference, out instance))
        {
            if (instance.PublicInputs.Count != 1)
                throw new ScriptException($"模块实例 {reference} 的 input 数量为 {instance.PublicInputs.Count}；必须明确选择唯一公开 input");
            nodeName = instance.PublicInputs.Values.Single();
        }
        else
        {
            instance = FindPublicOwner(reference, isOutput: false) ?? FindOwner(reference);
            if (instance == null)
                throw new ScriptException($"未知模块实例或 input {reference}");
            var input = reference.Substring(instance.InstanceName.Length + 1);
            if (!instance.PublicInputs.TryGetValue(input, out nodeName!))
                throw new ScriptException($"模块实例 {instance.InstanceName} 未公开 input {input}，不能进入私有内部节点");
        }
        if (!nodes.TryGetValue(nodeName, out var node))
            throw new ScriptException($"模块实例 {instance.InstanceName} 的 input {reference} 未定义真实节点 {nodeName}");
        return node;
    }

    private static bool PublishesExactEvent(IFSMNode node, string eventName)
    {
        node.UpdateEventDescriptions();
        // 引擎事件字典区分大小写；不能把私有 done 当作公开 Done 的实际发布者。
        return node.EventDescriptions.Any(branch => branch.Description == eventName);
    }

    public FsmModuleOutput ResolveOutput(string reference)
    {
        var instance = FindPublicOwner(reference, isOutput: true) ?? FindOwner(reference);
        if (instance == null)
            throw new ScriptException($"未知模块实例的 output {reference}；短写法只接受实例的公开 output");
        var output = reference.Substring(instance.InstanceName.Length + 1);
        if (!instance.PublicOutputs.TryGetValue(output, out var eventName)
            || !events.TryGetValue(eventName, out var @event))
            throw new ScriptException($"模块实例 {instance.InstanceName} 未公开 output {output}");
        var publishers = instance.ExpandedNodeNames.Where(nodes.ContainsKey).Select(name => nodes[name])
            .Where(n => PublishesExactEvent(n, eventName)).ToArray();
        if (publishers.Length == 0)
            throw new ScriptException($"模块实例 {instance.InstanceName} 的 output {output} 没有真实发布者");
        return new FsmModuleOutput(@event, publishers);
    }
}
