using Antlr4.Runtime.Misc;

namespace StateMachine.ScriptAnalyzer
{
    public static class StateMachineScriptAnalyzer
    {
        /// <summary>
        /// 读取脚本的声明视图，不展开模块模板。use 通过 IVisualNode 回调报告其实例元数据；
        /// 出口接线回调依次提供限定 Output、实例名及公开目标引用（保留显式 Input）。
        /// 同文件的 Output 归属按公开声明解析；外部模板缺失时，多个可能归属会明确失败而不猜测。
        /// 这是语法投影；完整接口合法性与实际发布者解析由 FSMEngine 的构建入口负责。
        /// </summary>
        public static void ReadScript(this string input,
            Action<IVisualNode> nodeCreateAction,
            Action<string, string, string> connectAction)
        {
            var tree = FsmScriptParser.Parse(input);

            var nodeListener = new NodeListener();
            var outputs = tree.module_statement().ToDictionary(
                module => module.STRING().Last().GetText(),
                module => module.module_body().output_declaration().SelectMany(output => output.STRING()).Select(name => name.GetText()).ToArray(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var import in tree.import_statement())
                if (import.STRING().Length == 2 && outputs.TryGetValue(import.STRING()[0].GetText(), out var names))
                    outputs[import.STRING()[1].GetText()] = names;
            var connectListener = new ConnectLineListener(nodeListener.ModuleInstances, nodeListener.NodeNames, outputs);

            try
            {
                nodeListener.StateCreated += nodeCreateAction;
                connectListener.LineConnected += connectAction;
                var walker = new Antlr4.Runtime.Tree.ParseTreeWalker();
                walker.Walk(nodeListener, tree);
                walker.Walk(connectListener, tree);
            }
            finally
            {
                nodeListener.StateCreated -= nodeCreateAction;
                connectListener.LineConnected -= connectAction;
            }

        }

    }

    internal class ConnectLineListener : StateMachineScriptBaseListener
    {
        public event Action<string, string, string>? LineConnected;

        private string nameprev = "";
        private int moduleDepth;
        private readonly Dictionary<string, FsmModuleInstance> instances;
        private readonly HashSet<string> nodes;
        private readonly Dictionary<string, string[]> declaredOutputs;
        private readonly FsmModuleInterfaceResolver interfaces;

        internal ConnectLineListener(Dictionary<string, FsmModuleInstance> instances, HashSet<string> nodes,
            Dictionary<string, string[]> declaredOutputs)
        {
            (this.instances, this.nodes, this.declaredOutputs) = (instances, nodes, declaredOutputs);
            interfaces = new FsmModuleInterfaceResolver(instances, new Dictionary<string, IFSMNode>(), new Dictionary<string, FSMEvent>());
        }

        public override void EnterModule_statement(StateMachineScriptParser.Module_statementContext context) => moduleDepth++;
        public override void ExitModule_statement(StateMachineScriptParser.Module_statementContext context) => moduleDepth--;

        private string Qualify(string reference)
        {
            var local = nameprev + reference;
            // 普通限定节点按实际声明解析，不能仅因点号前缀与某个实例同名就改换身份。
            if (nodes.Contains(local) || instances.ContainsKey(local)) return local;
            if (instances.ContainsKey(reference)) return reference;
            if (instances.Keys.Any(name => local.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)))
                return local;
            return instances.Keys.Any(name => reference == name || reference.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                ? reference : local;
        }

        public override void ExitModuleOutputTransition(StateMachineScriptParser.ModuleOutputTransitionContext context)
        {
            if (moduleDepth > 0) return;
            // 只投影声明元数据；公开名称归属复用引擎的权威解析，不展开模板或创建运行时对象。
            foreach (var metadata in instances.Values)
                if (declaredOutputs.TryGetValue(metadata.ModuleName, out var names) && metadata.PublicOutputs.Count == 0)
                    metadata.PublicOutputs = names.ToDictionary(name => name, name => metadata.InstanceName + "." + name, StringComparer.Ordinal);
            var reference = context.STRING()[0].GetText();
            var local = nameprev + reference;
            var localOwner = interfaces.FindPublicOwner(local, isOutput: true);
            var globalOwner = interfaces.FindPublicOwner(reference, isOutput: true);
            var instance = localOwner ?? globalOwner;
            var output = localOwner != null ? local : globalOwner != null ? reference : Qualify(reference);
            if (instance == null)
            {
                // 外部模块的模板不在此语法投影中；只有唯一未知候选时才可保留引用，不能猜最长前缀。
                var candidates = instances.Values.Where(candidate => !declaredOutputs.ContainsKey(candidate.ModuleName)
                    && output.StartsWith(candidate.InstanceName + ".", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length > 1)
                    throw new ScriptException($"模块出口 {output} 有多个可能归属；ReadScript 缺少外部模块公开接口，请通过 FSMEngine 构建解析");
                instance = candidates.SingleOrDefault();
            }
            if (instance == null)
                throw new ScriptException($"未知模块实例出口或未公开 Output {output}；短写法只用于模块公开 Output");
            LineConnected?.Invoke(output, instance.InstanceName, Qualify(context.STRING()[1].GetText()));
        }

        public override void EnterNamespace([NotNull] StateMachineScriptParser.NamespaceContext context)
        {
            nameprev = context.STRING().GetText() + ".";
            base.EnterNamespace(context);
        }

        public override void ExitNamespace([NotNull] StateMachineScriptParser.NamespaceContext context)
        {
            nameprev = "";
            base.ExitNamespace(context);
        }

        public override void ExitDefTransition([NotNull] StateMachineScriptParser.DefTransitionContext context)
        {
            if (moduleDepth > 0) return;
            var event_name = nameprev + context.STRING()[0].GetText();
            var sourceState_name = Qualify(context.STRING()[1].GetText());
            var targetState_name = Qualify(context.STRING()[2].GetText());

            LineConnected?.Invoke(event_name, sourceState_name, targetState_name);

            base.ExitDefTransition(context);
        }

    }
}
