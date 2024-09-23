using Antlr4.Runtime.Misc;

namespace StateMachine
{
    public class BuildStateVisitor : StateMachineScriptBaseVisitor<object>
    {
        public BuildStateVisitor(Dictionary<string, FSMEvent> eventDict, Dictionary<string, IFSMNode> nodeDict)
        {
            EventDict = eventDict;
            NodeDict = nodeDict;
            //if(!EventDict.ContainsKey("InteruptEvent"))
            //{
            //    EventDict.Add("InteruptEvent", new FSMEvent());
            //}
            //if (!EventDict.ContainsKey("EndEvent"))
            //{
            //    EventDict.Add("EndEvent", new FSMEvent());
            //}
        }

        private IFSMNode node;
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

        public override object VisitDefBranch([NotNull] StateMachineScriptParser.DefBranchContext context)
        {
            var index = context.INT().GetText();
            var event_name = nameprev + context.STRING().GetText();
            if (!EventDict.TryGetValue(event_name, out FSMEvent value))
            {
                value = new FSMEvent(event_name);
                EventDict.Add(event_name, value);
            }
            node.SetBranchEvent(int.Parse(index), value);
            return base.VisitDefBranch(context);
        }

        public override object VisitDefBranch2([NotNull] StateMachineScriptParser.DefBranch2Context context)
        {
            var index = 0;
            switch (context.branch_type.Type)
            {
                case StateMachineScriptParser.INT:
                    index = int.Parse(context.INT().GetText());
                    break;
                case StateMachineScriptParser.NONE:
                    index = 0;
                    break;
                case StateMachineScriptParser.SUCCESS:
                    index = 1;
                    break;
                case StateMachineScriptParser.FAILED:
                    index = 2;
                    break;
                case StateMachineScriptParser.ERROR:
                    index = 3;
                    break;
                case StateMachineScriptParser.BREAK:
                    index = 4;
                    break;
                case StateMachineScriptParser.CANCEL:
                    index = 5;
                    break;
            }
            var event_name = nameprev + context.STRING().GetText();
            if (!EventDict.TryGetValue(event_name, out FSMEvent value))
            {
                value = new FSMEvent(event_name);
                EventDict.Add(event_name, value);
            }
            node.SetBranchEvent(index, value);
            return base.VisitDefBranch2(context);
        }

        public override object VisitPOSX([NotNull] StateMachineScriptParser.POSXContext context)
        {
            if (context.DOUBLE() is null)
            {
                int.TryParse(context.INT().ToString(), out int px);
                node.PosX = px;
            }
            else
            {
                double.TryParse(context.DOUBLE().ToString(), out double px);
                node.PosX = px;
            }
            return base.VisitPOSX(context);
        }

        public override object VisitPOSY([NotNull] StateMachineScriptParser.POSYContext context)
        {
            if (context.DOUBLE() is null)
            {
                int.TryParse(context.INT().ToString(), out int py);
                node.PosY = py;
            }
            else
            {
                double.TryParse(context.DOUBLE().ToString(), out double py);
                node.PosY = py;
            }
            return base.VisitPOSY(context);
        }

        public override object VisitFlowIDDef([NotNull] StateMachineScriptParser.FlowIDDefContext context)
        {
            if (context.STRING() != null)
            {
                node.FlowID = context.STRING().GetText();
            }
            else if (context.INT() != null)
            {
                node.FlowID = context.INT().GetText();
            }
            return base.VisitFlowIDDef(context);
        }

        public override object VisitColorDef([NotNull] StateMachineScriptParser.ColorDefContext context)
        {
            node.Color = context.CODESTRING().GetText().Replace(@"""", "");
            return base.VisitColorDef(context);
        }

        public override object VisitDefEvent([NotNull] StateMachineScriptParser.DefEventContext context)
        {
            var event_name = nameprev + context.STRING().GetText();
            if (!EventDict.ContainsKey(event_name))
            {
                EventDict.Add(event_name, new FSMEvent(event_name));
            }
            return base.VisitDefEvent(context);
        }

        public override object VisitDefState([NotNull] StateMachineScriptParser.DefStateContext context)
        {
            var state_name = nameprev + context.STRING()[0].GetText();
            var state_type = context.STRING()[1].GetText();

            if (NodeDict.ContainsKey(state_name))
            {
                throw new ScriptException("State " + state_name + " 定义出错, " + "已存在相同名字的State！");
            }

            node = CreateNode(state_type);
            node.Name = state_name;
            node.NamePrev = nameprev;
            node.ClassType = state_type;

            NodeDict.Add(state_name, node);
            foreach (var branch in context.state_branch())
            {
                Visit(branch);
            }
            node = default!;
            return null;
        }

        public override object VisitDefState2([NotNull] StateMachineScriptParser.DefState2Context context)
        {
            var state_name = nameprev + context.STRING()[0].GetText();
            string state_type = context.STRING()[0].GetText();
            if (context.STRING().Length == 2)
                state_type = context.STRING()[1].GetText();

            if (NodeDict.ContainsKey(state_name))
            {
                throw new ScriptException("State " + state_name + " 定义出错, " + "已存在相同名字的State！");
            }
            node = CreateNode(state_type);
            node.Name = state_name;
            node.NamePrev = nameprev;
            node.ClassType = state_type;

            NodeDict.Add(state_name, node);
            foreach (var branch in context.state_branch())
            {
                Visit(branch);
            }
            node = default!;
            return null;// base.VisitDefState(context);
        }

        public override object VisitDefGroupState([NotNull] StateMachineScriptParser.DefGroupStateContext context)
        {
            var state_name = nameprev + context.STRING()[0].GetText();
            string state_type = "Group";
            var stState = nameprev + context.STRING()[1].GetText();
            var edEvent = nameprev + context.STRING()[2].GetText();

            if (NodeDict.ContainsKey(state_name))
            {
                throw new ScriptException("State " + state_name + " 定义出错, " + "已存在相同名字的State！");
            }
            node = new GroupNode(stState, edEvent);
            node.Name = state_name;
            node.NamePrev = nameprev;
            node.ClassType = state_type;

            NodeDict.Add(state_name, node);
            node = default!;
            return null;// base.VisitDefState(context);
        }

        protected virtual IFSMNode CreateNode(string state_type)
        {
            IFSMNode proc = default;
            //预定义的Node
            switch (state_type.ToLower())
            {
                default:
                    try
                    {
                        proc = IoC.Get<IFSMNode>(state_type);
                    }
                    catch (Exception)
                    { throw new ScriptException("State " + state_type + " 定义出错, " + "该State未注入IoC中！"); }
                    break;
            }
            return proc;
        }

    }
}
