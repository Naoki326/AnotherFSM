using System.Globalization;
using System.Text;
using StateMachine;

namespace FSMDemo.API.Services;

public partial class EngineManager
{
    // 模块序列化与平面调试 ToString 合同不同：使用公开接口还原边，不泄露内部节点。
    public string Export()
    {
        var sb = new StringBuilder();
        var instances = Engine.ModuleInstances.Values.ToArray();
        var nodeOwners = instances.SelectMany(i => i.ExpandedNodeNames.Select(n => (Name: n, Instance: i)))
            .ToDictionary(p => p.Name, p => p.Instance, StringComparer.Ordinal);
        var internalEvents = instances.SelectMany(i => i.ExpandedEventNames).ToHashSet(StringComparer.Ordinal);

        foreach (var moduleName in Engine.GetModuleNames())
        {
            if (!Engine.TryGetModule(moduleName, out var template)) continue;
            if (template.Terminals.Count > 0)
                throw CannotExport($"模块 {moduleName} 仍含旧 terminal 配置");
            sb.AppendLine($"module {moduleName} {{");
            if (template.Inputs.Count > 0) sb.AppendLine($"input {string.Join(", ", template.Inputs)};");
            if (template.Outputs.Count > 0) sb.AppendLine($"output {string.Join(", ", template.Outputs)};");
            foreach (var eventName in template.TemplateEvents.Keys) sb.AppendLine($"def {eventName} event;");
            foreach (var (name, node) in template.TemplateNodes) AppendNode(sb, name, node, "");
            foreach (var edge in template.PendingTransitions)
                sb.AppendLine($"{edge.EventName} -> {edge.SourceNode} to {edge.TargetNode};");
            sb.AppendLine("}");
        }

        foreach (var instance in instances)
        {
            if (instance.OutputEventMap.Count > 0 || !Engine.TryGetModule(instance.ModuleName, out var template)
                || !instance.PublicInputs.Keys.ToHashSet().SetEquals(template.Inputs)
                || !instance.PublicOutputs.Keys.ToHashSet().SetEquals(template.Outputs))
                throw CannotExport($"实例 {instance.InstanceName} 使用历史映射或缺少公开接口元数据");
            foreach (var (localName, templateNode) in template.TemplateNodes)
            {
                var expanded = Engine.GetNode(instance.InstanceName + "." + localName);
                templateNode.UpdateEventDescriptions();
                expanded.UpdateEventDescriptions();
                var expectedBranches = templateNode.EventDescriptions
                    .Select(b => (b.Index, Event: instance.InstanceName + "." + b.Description)).ToHashSet();
                if (!expectedBranches.SetEquals(expanded.EventDescriptions.Select(b => (b.Index, b.Description))))
                    throw CannotExport($"实例 {instance.InstanceName} 的 Branch 与模板不一致");
            }
            foreach (var expected in template.PendingTransitions)
            {
                var source = Engine.GetNode(instance.InstanceName + "." + expected.SourceNode);
                var trigger = Engine.GetEvent(instance.InstanceName + "." + expected.EventName);
                if (!source.HasTransition(trigger) || source.TargetState(trigger).Name != instance.InstanceName + "." + expected.TargetNode)
                    throw CannotExport($"实例 {instance.InstanceName} 的内部连接与模板不一致");
            }
            var namespacePrefixes = instance.ExpandedNodeNames.Select(n => Engine.GetNode(n).NamePrefix).Distinct().ToArray();
            var namespacePrefix = namespacePrefixes.Length == 1 ? namespacePrefixes[0] : "";
            if (namespacePrefix.Length > 0) sb.AppendLine($"namespace {namespacePrefix.TrimEnd('.')} {{");
            sb.AppendLine($"use {instance.ModuleName} as {RemovePrefix(instance.InstanceName, namespacePrefix)} {{");
            AppendDisplayMetadata(sb, instance.PosX, instance.PosY, instance.Color, instance.FlowID);
            sb.AppendLine("}");
            if (namespacePrefix.Length > 0) sb.AppendLine("}");
        }
        foreach (var eventName in Engine.GetEventNames().Where(n => !internalEvents.Contains(n)))
            sb.AppendLine($"def {eventName} event;");
        foreach (var name in Engine.GetNodeNames().Where(n => !nodeOwners.ContainsKey(n)))
        {
            var node = Engine.GetNode(name);
            var prefix = node.NamePrefix;
            if (prefix.Length > 0)
            {
                sb.AppendLine($"namespace {prefix.TrimEnd('.')} {{");
                AppendNode(sb, RemovePrefix(name, prefix), node, prefix);
                sb.AppendLine("}");
            }
            else AppendNode(sb, name, node, "");
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Engine.GetNodeNames())
        foreach (var edge in Engine.GetNode(name).GetFSMTransitions())
        {
            nodeOwners.TryGetValue(name, out var sourceInstance);
            nodeOwners.TryGetValue(edge.Target.Name, out var targetInstance);
            if (sourceInstance != null && ReferenceEquals(sourceInstance, targetInstance)
                && IsTemplateTransition(sourceInstance, name, edge.Trigger.EventName, edge.Target.Name)) continue;

            string target;
            try { target = targetInstance == null ? edge.Target.Name : PublicTarget(targetInstance, edge.Target.Name); }
            catch (InvalidOperationException ex) { throw CannotExport(ex.Message); }
            string line;
            if (sourceInstance == null)
            {
                if (internalEvents.Contains(edge.Trigger.EventName))
                    throw CannotExport($"普通节点 {name} 使用模块内部事件 {edge.Trigger.EventName}");
                line = $"{edge.Trigger.EventName} -> {name} to {target};";
            }
            else
            {
                var outputName = sourceInstance.PublicOutputs.SingleOrDefault(p => p.Value == edge.Trigger.EventName).Key;
                if (outputName == null) throw CannotExport($"实例 {sourceInstance.InstanceName} 的私有事件 {edge.Trigger.EventName} 连接到外部");
                var reference = sourceInstance.InstanceName + "." + outputName;
                var output = Engine.ModuleInterfaces.ResolveOutput(reference);
                // 一条接口边必须代表所有实际发布者；历史的部分发布者接线不可静默扩大。
                if (!output.Publishers.All(p => p.HasTransition(output.Event)
                    && p.TargetState(output.Event).Name == edge.Target.Name))
                    throw CannotExport($"出口 {reference} 的实际发布者连接不一致");
                line = $"{reference} -> {target};";
            }
            if (emitted.Add(line)) sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private bool IsTemplateTransition(FsmModuleInstance instance, string source, string eventName, string target)
    {
        if (!Engine.TryGetModule(instance.ModuleName, out var template)) return false;
        var prefix = instance.InstanceName + ".";
        return template.PendingTransitions.Any(t => prefix + t.SourceNode == source
            && prefix + t.EventName == eventName && prefix + t.TargetNode == target);
    }

    private void AppendNode(StringBuilder sb, string name, IFSMNode node, string prefix)
    {
        node.UpdateEventDescriptions();
        sb.AppendLine($"def {name}({node.ClassType}) {{");
        foreach (var branch in node.EventDescriptions)
            sb.AppendLine($"{branch.Index}->{RemovePrefix(branch.Description, prefix)};");
        var groups = GetGroupDefs(node);
        if (groups != null)
            foreach (var group in groups) sb.AppendLine($"[{group.StartNode}->{group.EndEvent}];");
        AppendDisplayMetadata(sb, node.PosX, node.PosY, node.Color, node.FlowID);
        sb.AppendLine("}");
    }

    private static void AppendDisplayMetadata(StringBuilder sb, double x, double y, string color, string flowId)
    {
        // DSL 数字固定小数点；不依赖运行机器的区域设置。
        sb.AppendLine($"Pos:({x.ToString("0.################", CultureInfo.InvariantCulture)},{y.ToString("0.################", CultureInfo.InvariantCulture)});");
        if (color.Contains('"') || color.Contains('\n') || color.Contains('\r'))
            throw CannotExport("显示颜色不能包含引号或换行");
        sb.AppendLine($"Color:\"{color}\";");
        sb.AppendLine($"FlowID:{flowId};");
    }

    private static string RemovePrefix(string name, string prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
            throw CannotExport($"名称 {name} 无法在 Namespace {prefix} 中表达");
        return name.Substring(prefix.Length);
    }

    private static InvalidOperationException CannotExport(string reason)
        => new($"无法按新模块 DSL 导出：{reason}；请使用公开 Input/Output 和 use 声明，不支持历史映射图。");
}
