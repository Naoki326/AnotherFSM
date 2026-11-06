using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace StateMachine;

internal class BuildStateVisitor : StateMachineScriptBaseVisitor<object>
{
    private readonly IFSMNodeFactory nodeFactory;
    private readonly FSMEngine engine;
    private IFSMNode? node;
    private string nameprev = "";
    private FsmModuleInstance? currentInstance;
    private FsmModuleInfo? currentTemplate;
    private readonly HashSet<string> loadingStack;
    private readonly string? scriptDirectory;
    private readonly FsmModuleInfo? loadingModule;
    private readonly HashSet<string> declaredModuleNames = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, FSMEvent> EventDict;
    public Dictionary<string, IFSMNode> NodeDict;

    public BuildStateVisitor(Dictionary<string, FSMEvent> eventDict, Dictionary<string, IFSMNode> nodeDict,
        IFSMNodeFactory nodeFactory, FSMEngine engine, HashSet<string>? loadingStack = null,
        string? directoryPath = null, FsmModuleInfo? loadingModule = null)
    {
        EventDict = eventDict;
        NodeDict = nodeDict;
        this.nodeFactory = nodeFactory;
        this.engine = engine;
        scriptDirectory = directoryPath ?? engine.CurrentScriptDirectory;
        this.loadingModule = loadingModule;
        this.loadingStack = loadingStack ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public override object? VisitNamespace(StateMachineScriptParser.NamespaceContext context)
    {
        var saved = nameprev;
        nameprev = context.STRING().GetText() + ".";
        foreach (var expression in context.expression()) Visit(expression);
        nameprev = saved;
        return null;
    }

    public override object? VisitImport_statement(StateMachineScriptParser.Import_statementContext context)
    {
        var names = context.STRING();
        var import = names[0].GetText();
        engine.LoadModuleWithDependencies(import, scriptDirectory ?? ".", loadingStack);
        declaredModuleNames.Add(import);
        if (names.Length == 2 && engine.TryGetModule(import, out var info))
        {
            var alias = names[1].GetText();
            // 同一模块在新一轮脚本构建中会产生新模板；别名应刷新，而不是按对象引用误判碰撞。
            if (engine.TryGetModule(alias, out var existing) && !ReferenceEquals(existing, info)
                && !string.Equals(existing.ModuleName, info.ModuleName, StringComparison.OrdinalIgnoreCase))
                throw new ScriptException($"模块 import 别名 {alias} 与已有模块名称碰撞，不能覆盖已有定义");
            engine.moduleRegistry[alias] = info;
            declaredModuleNames.Add(alias);
        }
        return null;
    }

    public override object? VisitModule_statement(StateMachineScriptParser.Module_statementContext context)
    {
        var names = context.STRING();
        var registryName = names[names.Length - 1].GetText();
        if (!declaredModuleNames.Add(registryName)
            || (engine.TryGetModule(registryName, out var existing) && !ReferenceEquals(existing, loadingModule)
                && (!string.IsNullOrEmpty(existing.ScriptContent)
                    || !string.Equals(existing.ModuleName, registryName, StringComparison.OrdinalIgnoreCase))))
            throw new ScriptException($"模块声明 {registryName} 与已有模块或 import 别名名称碰撞，不能覆盖已有定义");
        var template = new FsmModuleInfo
        {
            ModuleName = registryName,
            DirectoryPath = scriptDirectory,
            IsParsed = true,
        };
        var savedNodes = NodeDict;
        var savedEvents = EventDict;
        var savedPrefix = nameprev;
        var savedTemplate = currentTemplate;
        NodeDict = template.TemplateNodes;
        EventDict = template.TemplateEvents;
        nameprev = "";
        currentTemplate = template;
        try
        {
            Visit(context.module_body());
            foreach (var input in template.Inputs)
                if (!NodeDict.ContainsKey(input))
                    throw new ScriptException($"模块 {template.ModuleName} 的 input {input} 未定义节点");
        }
        finally
        {
            NodeDict = savedNodes;
            EventDict = savedEvents;
            nameprev = savedPrefix;
            currentTemplate = savedTemplate;
        }
        engine.moduleRegistry[template.ModuleName] = template;
        return null;
    }

    public override object? VisitInput_declaration(StateMachineScriptParser.Input_declarationContext context)
    {
        foreach (var name in context.STRING())
        {
            if (currentTemplate!.Inputs.Contains(name.GetText(), StringComparer.Ordinal))
                throw new ScriptException($"模块 {currentTemplate.ModuleName} 的 input {name.GetText()} 重复声明");
            currentTemplate.Inputs.Add(name.GetText());
        }
        return null;
    }

    public override object? VisitOutput_declaration(StateMachineScriptParser.Output_declarationContext context)
    {
        foreach (var name in context.STRING())
        {
            if (currentTemplate!.Outputs.Contains(name.GetText(), StringComparer.Ordinal))
                throw new ScriptException($"模块 {currentTemplate.ModuleName} 的 output {name.GetText()} 重复声明");
            currentTemplate.Outputs.Add(name.GetText());
            GetOrAddEvent(name.GetText());
        }
        return null;
    }

    // 保留诊断规则和公共模型字段，但新 DSL 不再使用 terminal 或事件映射。
    public override object? VisitTerminal_declaration(StateMachineScriptParser.Terminal_declarationContext context)
        => throw new ScriptException($"模块 {currentTemplate?.ModuleName} 的旧 terminal 声明已淘汰；请声明 output 并使用实例.Output -> Target;");

    public override object? VisitNamedOutputMapDef(StateMachineScriptParser.NamedOutputMapDefContext context)
        => throw new ScriptException($"旧 output 映射 {context.GetText()} 已淘汰；请使用 use 声明和实例.Output -> Target;");

    public override object? VisitDefTransition(StateMachineScriptParser.DefTransitionContext context)
    {
        if (currentTemplate != null)
        {
            GetOrAddEvent(context.STRING()[0].GetText());
            currentTemplate.PendingTransitions.Add(new TemplateTransition
            {
                EventName = context.STRING()[0].GetText(),
                SourceNode = context.STRING()[1].GetText(),
                TargetNode = context.STRING()[2].GetText(),
            });
        }
        return null;
    }

    public override object? VisitModuleOutputTransition(StateMachineScriptParser.ModuleOutputTransitionContext context)
    {
        if (currentTemplate != null)
            throw new ScriptException($"模块 {currentTemplate.ModuleName} 内部应使用普通事件优先转移，不支持嵌套模块出口接线 {context.GetText()}");
        return null;
    }

    public override object? VisitModule_instance(StateMachineScriptParser.Module_instanceContext context)
    {
        if (currentTemplate != null)
            throw new ScriptException($"模块 {currentTemplate.ModuleName} 内不支持嵌套 use 实例");
        var moduleName = context.STRING()[0].GetText();
        var instanceName = nameprev + context.STRING()[1].GetText();
        if (!engine.TryGetModule(moduleName, out var template))
            throw new ScriptException($"模块 {moduleName} 未注册，无法创建实例 {instanceName}，请先定义或 import");
        if (!template.IsParsed && !string.IsNullOrEmpty(template.ScriptContent))
        {
            engine.LoadModuleWithDependencies(moduleName, template.DirectoryPath ?? scriptDirectory ?? ".", loadingStack);
            engine.TryGetModule(moduleName, out template);
        }
        var expandedNodes = template.TemplateNodes.Keys.Select(name => instanceName + "." + name).ToArray();
        var expandedEvents = template.TemplateEvents.Keys.Select(name => instanceName + "." + name).ToArray();
        if (engine.moduleInstances.ContainsKey(instanceName)
            || NodeDict.Keys.Contains(instanceName, StringComparer.OrdinalIgnoreCase)
            || expandedNodes.Any(name => NodeDict.ContainsKey(name)
                || engine.moduleInstances.ContainsKey(name))
            || expandedEvents.Any(name => EventDict.ContainsKey(name)))
            throw new ScriptException($"模块实例 {instanceName} 名称碰撞：实例及其限定节点和事件必须独立");
        var instance = new FsmModuleInstance { InstanceName = instanceName, ModuleName = template.ModuleName };
        currentInstance = instance;
        try
        {
            foreach (var branch in context.state_branch())
            {
                if (branch is not StateMachineScriptParser.PosDefContext
                    and not StateMachineScriptParser.ColorDefContext
                    and not StateMachineScriptParser.FlowIDDefContext)
                    throw new ScriptException($"模块实例 {instanceName} 的 use 属性只允许 Pos、Color、FlowID；旧 output 映射请改用 {instanceName}.Output -> Target;");
                Visit(branch);
            }
        }
        finally { currentInstance = null; }
        ExpandModuleInstance(instance, template);
        return null;
    }

    public override object? VisitDefState(StateMachineScriptParser.DefStateContext context)
        => DefineNode(context.STRING().Select(s => s.GetText()).ToArray(), context.state_branch());

    public override object? VisitDefState2(StateMachineScriptParser.DefState2Context context)
        => DefineNode(context.STRING().Select(s => s.GetText()).ToArray(), context.state_branch());

    private object? DefineNode(string[] names, StateMachineScriptParser.State_branchContext[] branches)
    {
        var name = AnotherFSM.Syntax.DeclarationRules.Qualify(nameprev, names[0]);
        EnsureNotModuleOwned(name, isNode: true);
        if (NodeDict.ContainsKey(name))
            throw new ScriptException($"State {name} 定义出错，已存在相同名字的 State");
        node = CreateNode(AnotherFSM.Syntax.DeclarationRules.TypeKey(names[0], names.Length == 1 ? null : names[names.Length - 1]), name);
        node.Name = name;
        node.NamePrefix = nameprev;
        NodeDict.Add(name, node);
        try { foreach (var branch in branches) Visit(branch); }
        finally { node = null; }
        return null;
    }

    public override object? VisitDefBranch(StateMachineScriptParser.DefBranchContext context)
    {
        EnsureNotModuleOwned(nameprev + context.STRING().GetText());
        node!.SetBranchEvent(int.Parse(context.INT().GetText()), GetOrAddEvent(nameprev + context.STRING().GetText()));
        return null;
    }

    public override object? VisitDefBranch2(StateMachineScriptParser.DefBranch2Context context)
    {
        var index = context.branch_type.Type switch
        {
            StateMachineScriptParser.INT => int.Parse(context.INT().GetText()),
            StateMachineScriptParser.SUCCESS => 1,
            StateMachineScriptParser.FAILED => 2,
            StateMachineScriptParser.ERROR => 3,
            StateMachineScriptParser.BREAK => 4,
            StateMachineScriptParser.CANCEL => 5,
            _ => 0,
        };
        EnsureNotModuleOwned(nameprev + context.STRING().GetText());
        node!.SetBranchEvent(index, GetOrAddEvent(nameprev + context.STRING().GetText()));
        return null;
    }

    public override object? VisitDefEvent(StateMachineScriptParser.DefEventContext context)
    {
        EnsureNotModuleOwned(nameprev + context.STRING().GetText());
        GetOrAddEvent(nameprev + context.STRING().GetText());
        return null;
    }

    public override object? VisitPOSX(StateMachineScriptParser.POSXContext context)
    {
        var value = double.Parse(context.GetText(), System.Globalization.CultureInfo.InvariantCulture);
        if (currentInstance != null) currentInstance.PosX = value;
        else if (node != null) node.PosX = value;
        return null;
    }

    public override object? VisitPOSY(StateMachineScriptParser.POSYContext context)
    {
        var value = double.Parse(context.GetText(), System.Globalization.CultureInfo.InvariantCulture);
        if (currentInstance != null) currentInstance.PosY = value;
        else if (node != null) node.PosY = value;
        return null;
    }

    public override object? VisitColorDef(StateMachineScriptParser.ColorDefContext context)
    {
        var value = context.CODESTRING().GetText().Trim('"');
        if (currentInstance != null) currentInstance.Color = value;
        else if (node != null) node.Color = value;
        return null;
    }

    public override object? VisitFlowIDDef(StateMachineScriptParser.FlowIDDefContext context)
    {
        var value = context.GUID()?.GetText() ?? context.STRING()?.GetText() ?? context.INT().GetText();
        if (currentInstance != null) currentInstance.FlowID = value;
        else if (node != null) node.FlowID = value;
        return null;
    }

    private void EnsureNotModuleOwned(string name, bool isNode = false)
    {
        if (currentTemplate != null) return;
        var owned = engine.moduleInstances.Values.Any(instance =>
            (isNode ? instance.ExpandedNodeNames : instance.ExpandedEventNames)
                .Contains(name));
        if (owned || (isNode && engine.moduleInstances.ContainsKey(name)))
            throw new ScriptException($"名称 {name} 与模块实例的实际限定对象碰撞；请使用公开接口接线");
    }

    private FSMEvent GetOrAddEvent(string name)
    {
        if (!EventDict.TryGetValue(name, out var value))
            EventDict.Add(name, value = new FSMEvent(name));
        return value;
    }

    private void ExpandModuleInstance(FsmModuleInstance instance, FsmModuleInfo template)
    {
        var prefix = instance.InstanceName + ".";
        // 出口事件始终保留实例限定身份；不创建外部别名，也不重绑 Branch。
        foreach (var eventName in template.TemplateEvents.Keys)
        {
            GetOrAddEvent(prefix + eventName);
            instance.ExpandedEventNames.Add(prefix + eventName);
        }
        foreach (var pair in template.TemplateNodes)
        {
            var source = pair.Value;
            var name = prefix + pair.Key;
            if (NodeDict.ContainsKey(name))
                throw new ScriptException($"模块实例 {instance.InstanceName} 节点 {name} 名称碰撞");
            var target = CreateNode(source.ClassType, name);
            target.Name = name;
            target.NamePrefix = nameprev;
            target.PosX = source.PosX;
            target.PosY = source.PosY;
            target.Color = source.Color;
            source.UpdateEventDescriptions();
            foreach (var branch in source.EventDescriptions)
                target.SetBranchEvent(branch.Index, GetOrAddEvent(prefix + branch.Description));
            NodeDict.Add(name, target);
            instance.ExpandedNodeNames.Add(name);
        }
        foreach (var input in template.Inputs)
        {
            instance.PublicInputs.Add(input, prefix + input);
            instance.InputNodeNames.Add(prefix + input);
        }
        foreach (var output in template.Outputs)
            instance.PublicOutputs.Add(output, prefix + output);
        foreach (var transition in template.PendingTransitions)
            BuildTransitionVisitor.Connect(NodeDict, prefix + transition.SourceNode,
                GetOrAddEvent(prefix + transition.EventName), prefix + transition.TargetNode);
        engine.moduleInstances.Add(instance.InstanceName, instance);
    }

    protected virtual IFSMNode CreateNode(string stateType, string name)
    {
        try
        {
            var result = engine.CreateOwnedNode(stateType, name);
            result.ClassType = stateType;
            return result;
        }
        catch (FSMException) { throw; }
        catch (Exception ex)
        {
            if (engine.ContainsModule(stateType))
                throw new ScriptException($"模块 {stateType} 不再支持旧 def 实例声明；请使用 use {stateType} as Instance;");
            throw new ScriptException($"Node {name}, key {stateType}: factory creation failed: {ex.GetBaseException().Message}", ex);
        }
    }
}
