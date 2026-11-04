using Antlr4.Runtime.Misc;

namespace StateMachine.ScriptAnalyzer
{
    public static class StateMachineScriptAnalyzer
    {
        /// <summary>
        /// 读取脚本的声明视图，不展开模块模板。use 通过 IVisualNode 回调报告其实例元数据；
        /// 出口接线回调依次提供限定 Output、实例名及公开目标引用（保留显式 Input）。
        /// 这是语法投影；接口合法性与实际发布者解析由 FSMEngine 的构建入口负责。
        /// </summary>
        public static void ReadScript(this string input,
            Action<IVisualNode> nodeCreateAction,
            Action<string, string, string> connectAction)
        {
            var tree = FsmScriptParser.Parse(input);

            var nodeListener = new NodeListener();
            var connectListener = new ConnectLineListener(nodeListener.ModuleInstanceNames, nodeListener.NodeNames);

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
        private readonly HashSet<string> instances;

        private readonly HashSet<string> nodes;

        internal ConnectLineListener(HashSet<string> instances, HashSet<string> nodes)
            => (this.instances, this.nodes) = (instances, nodes);

        public override void EnterModule_statement(StateMachineScriptParser.Module_statementContext context) => moduleDepth++;
        public override void ExitModule_statement(StateMachineScriptParser.Module_statementContext context) => moduleDepth--;

        private string Qualify(string reference)
        {
            var local = nameprev + reference;
            // 普通限定节点按实际声明解析，不能仅因点号前缀与某个实例同名就改换身份。
            if (nodes.Contains(local) || instances.Contains(local)) return local;
            if (instances.Contains(reference)) return reference;
            if (instances.Any(name => local.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)))
                return local;
            return instances.Any(name => reference == name || reference.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                ? reference : local;
        }

        public override void ExitModuleOutputTransition(StateMachineScriptParser.ModuleOutputTransitionContext context)
        {
            if (moduleDepth > 0) return;
            var output = Qualify(context.STRING()[0].GetText());
            var instance = instances.Where(name => output.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name.Length).FirstOrDefault();
            if (instance == null)
                throw new ScriptException($"未知模块实例出口 {output}；短写法只用于模块公开 Output");
            LineConnected?.Invoke(output, instance, Qualify(context.STRING()[1].GetText()));
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
