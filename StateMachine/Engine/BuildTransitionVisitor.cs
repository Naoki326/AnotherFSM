using Antlr4.Runtime.Misc;

namespace StateMachine
{
    public class BuildTransitionVisitor : StateMachineScriptBaseVisitor<object>
    {
        public BuildTransitionVisitor(Dictionary<string, FSMEvent> eventDict, Dictionary<string, IFSMNode> nodeDict, Dictionary<string, FsmModuleInstance> moduleInstances)
        {
            EventDict = eventDict;
            NodeDict = nodeDict;
            ModuleInstances = moduleInstances;
        }

        public Dictionary<string, FSMEvent> EventDict;
        public Dictionary<string, IFSMNode> NodeDict;
        public Dictionary<string, FsmModuleInstance> ModuleInstances;

        private string nameprev = "";

        private List<string> ResolveSourceNodes(string sourceStateName, string eventName)
        {
            if (!ModuleInstances.TryGetValue(sourceStateName, out var instance))
                return [sourceStateName];

            // 优先用 EventToInternalNodes 精确匹配：它记录了 branch 引用 output 的真实节点，
            // 是基于实际 branch 设置的权威映射。
            if (instance.ExternalEventToInternalNodes.TryGetValue(eventName, out var outputNodes)
                && outputNodes.Count > 0)
            {
                return outputNodes;
            }

            if (instance.TerminalNodeNames.Count > 0)
            {
                var matchingTerminals = instance.TerminalNodeNames
                    .Where(nodeName => NodeDict.TryGetValue(nodeName, out var node)
                        && FSMNodeBranchEventHelper.PublishesEvent(node, eventName))
                    .ToList();

                if (matchingTerminals.Count > 0)
                    return matchingTerminals;

                throw new ScriptException("模块实例 " + sourceStateName + " 连线出错, 没有 terminal 节点发布事件 " + eventName + "！");
            }

            throw new ScriptException("模块实例 " + sourceStateName + " 连线出错, 未声明 terminal 节点！");
        }

        private List<string> ResolveTargetNodes(string targetStateName)
        {
            if (!ModuleInstances.TryGetValue(targetStateName, out var instance))
                return [targetStateName];

            if (instance.InputNodeNames.Count > 0)
                return instance.InputNodeNames;

            throw new ScriptException("模块实例 " + targetStateName + " 连线出错, 未声明 input 节点！");
        }

        private bool IsTerminalSourceNode(string sourceStateName, string sourceNodeName)
        {
            return ModuleInstances.TryGetValue(sourceStateName, out var instance)
                && instance.TerminalNodeNames.Any(nodeName =>
                    string.Equals(nodeName, sourceNodeName, StringComparison.OrdinalIgnoreCase));
        }

        public override object? VisitNamespace([NotNull] StateMachineScriptParser.NamespaceContext context)
        {
            nameprev = context.STRING().ToString() + ".";
            foreach (var c in context.expression())
            {
                Visit(c);
            }
            nameprev = "";
            return null;
        }

        // 跳过 module 定义体（模板连线由 BuildStateVisitor 在展开时处理）
        public override object VisitModule_statement([NotNull] StateMachineScriptParser.Module_statementContext context)
        {
            return null;
        }

        // import 语句在 Pass 1 已由 BuildStateVisitor 处理，Pass 2 跳过
        public override object VisitImport_statement([NotNull] StateMachineScriptParser.Import_statementContext context)
        {
            return null;
        }

        public override object VisitDefTransition([NotNull] StateMachineScriptParser.DefTransitionContext context)
        {
            var event_name = nameprev + context.STRING()[0].GetText();
            var sourceState_name = nameprev + context.STRING()[1].GetText();
            var targetState_name = nameprev + context.STRING()[2].GetText();

            var sourceNodeNames = ResolveSourceNodes(sourceState_name, event_name);
            var targetNodeNames = ResolveTargetNodes(targetState_name);

            if (!EventDict.ContainsKey(event_name))
            {
                EventDict.Add(event_name, new FSMEvent(event_name));
            }

            foreach (var sourceNodeName in sourceNodeNames)
            {
                if (!NodeDict.ContainsKey(sourceNodeName))
                {
                    throw new ScriptException("State " + sourceNodeName + " 连线出错, 未定义该State！");
                }

                foreach (var targetNodeName in targetNodeNames)
                {
                    if (!NodeDict.ContainsKey(targetNodeName))
                    {
                        throw new ScriptException("State " + targetNodeName + " 连线出错, 未定义该State！");
                    }

                    // 在同一状态下，一个事件只能导向一个状态；但是允许多个事件导向同一个状态。
                    if (NodeDict[sourceNodeName].HasTransition(EventDict[event_name]))
                    {
                        throw new ScriptException("State " + sourceNodeName + " 向 " + event_name + " 连线出错, 已定义从该状态到该事件连线！");
                    }

                    if (IsTerminalSourceNode(sourceState_name, sourceNodeName)
                        && !FSMNodeBranchEventHelper.EnsurePublishesEvent(NodeDict[sourceNodeName], EventDict[event_name]))
                    {
                        throw new ScriptException("模块实例 " + sourceState_name + " 的 terminal 节点 " + sourceNodeName + " 有多个分支，无法自动匹配外部事件 " + event_name + "！");
                    }

                    NodeDict[sourceNodeName].AddTransition(EventDict[event_name], NodeDict[targetNodeName]);
                }
            }

            return base.VisitDefTransition(context);
        }

    }
}
