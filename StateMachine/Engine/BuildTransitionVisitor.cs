using Antlr4.Runtime.Misc;

namespace StateMachine
{
    public class BuildTransitionVisitor : StateMachineScriptBaseVisitor<object>
    {
        public BuildTransitionVisitor(Dictionary<string, FSMEvent> eventDict, Dictionary<string, IFSMNode> nodeDict)
        {
            EventDict = eventDict;
            NodeDict = nodeDict;
        }

        public Dictionary<string, FSMEvent> EventDict;
        public Dictionary<string, IFSMNode> NodeDict;

        private string nameprev = "";
        public override object VisitNamespace([NotNull] StateMachineScriptParser.NamespaceContext context)
        {
            nameprev = context.STRING().ToString() + ".";
            foreach (var c in context.expression())
            {
                Visit(c);
            }
            nameprev = "";
            return null;
        }

        public override object VisitDefTransition([NotNull] StateMachineScriptParser.DefTransitionContext context)
        {
            var event_name = nameprev + context.STRING()[0].GetText();
            var sourceState_name = nameprev + context.STRING()[1].GetText();
            var targetState_name = nameprev + context.STRING()[2].GetText();

            if (!EventDict.ContainsKey(event_name))
            {
                EventDict.Add(event_name, new FSMEvent(event_name));
            }
            if (!NodeDict.ContainsKey(sourceState_name))
            {
                throw new ScriptException("State " + sourceState_name + " 连线出错, " + "未定义该State！");
            }
            if (!NodeDict.ContainsKey(targetState_name))
            {
                throw new ScriptException("State " + targetState_name + " 连线出错, " + "未定义该State！");
            }
            //在同一状态下，一个事件只能导向一个状态
            //但是允许多个事件导向同一个状态
            if (NodeDict[sourceState_name].HasTransition(EventDict[event_name]))
            {
                throw new ScriptException("State " + targetState_name + "向" + event_name + " 连线出错, " + "已定义从该状态到该事件连线！");
            }

            NodeDict[sourceState_name].AddTransition(EventDict[event_name], NodeDict[targetState_name]);

            return base.VisitDefTransition(context);
        }

    }
}

