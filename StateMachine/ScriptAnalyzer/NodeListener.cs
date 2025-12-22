using Antlr4.Runtime.Misc;

namespace StateMachine.ScriptAnalyzer
{

    internal class NodeListener : StateMachineScriptBaseListener
    {
        public event Action<IVisualNode> StateCreated;

        private string nameprev;

        public override void EnterNamespace([NotNull] StateMachineScriptParser.NamespaceContext context)
        {
            nameprev = context.STRING().ToString() + ".";
            base.EnterNamespace(context);
        }

        public override void ExitNamespace([NotNull] StateMachineScriptParser.NamespaceContext context)
        {
            nameprev = "";
            base.ExitNamespace(context);
        }

        #region DefNode

        private IVisualNode currentNode;

        public override void EnterDefState([NotNull] StateMachineScriptParser.DefStateContext context)
        {
            var state_name = nameprev + context.STRING()[0].GetText();
            string state_type = context.STRING()[0].GetText();
            if (context.STRING().Length == 2)
                state_type = context.STRING()[1].GetText();

            currentNode = new VisualNode()
            {
                ClassType = state_type,
                Name = state_name,
                NamePrefix = nameprev,
            };
            base.EnterDefState(context);
        }

        public override void EnterDefState2([NotNull] StateMachineScriptParser.DefState2Context context)
        {
            var state_name = nameprev + context.STRING()[0].GetText();
            string state_type = context.STRING()[0].GetText();
            if (context.STRING().Length == 2)
                state_type = context.STRING()[1].GetText();

            currentNode = new VisualNode()
            {
                ClassType = state_type,
                Name = state_name,
                NamePrefix = nameprev,
            };
            base.EnterDefState2(context);
        }

        public override void ExitDefState([NotNull] StateMachineScriptParser.DefStateContext context)
        {
            StateCreated?.Invoke(currentNode);
            base.ExitDefState(context);
        }

        public override void ExitDefState2([NotNull] StateMachineScriptParser.DefState2Context context)
        {
            StateCreated?.Invoke(currentNode);
            base.ExitDefState2(context);
        }

        #endregion

        #region Node Property

        public override void ExitColorDef([NotNull] StateMachineScriptParser.ColorDefContext context)
        {
            currentNode.Color = context.CODESTRING().ToString();
            base.ExitColorDef(context);
        }

        public override void ExitPosDef([NotNull] StateMachineScriptParser.PosDefContext context)
        {
            string posxStr = context.position().posx().ToString();
            string posyStr = context.position().posy().ToString();
            currentNode.PosX = double.Parse(posxStr);
            currentNode.PosY = double.Parse(posyStr);
            base.ExitPosDef(context);
        }

        public override void ExitFlowIDDef([NotNull] StateMachineScriptParser.FlowIDDefContext context)
        {
            if (context.GUID() != null)
            {
                currentNode.FlowID = context.GUID().GetText();
            }
            else if (context.INT() != null)
            {
                currentNode.FlowID = context.INT().GetText();
            }
            else if (context.STRING() != null)
            {
                currentNode.FlowID = context.STRING().GetText();
            }
            base.ExitFlowIDDef(context);
        }

        public override void ExitDefBranch([NotNull] StateMachineScriptParser.DefBranchContext context)
        {
            var index = context.INT().GetText();
            var event_name = nameprev + context.STRING().GetText();
            currentNode.EventDescriptions.Add(new NodeEventDescription()
            {
                Index = int.Parse(index),
                Description = event_name
            });
            base.ExitDefBranch(context);
        }

        public override void ExitDefBranch2([NotNull] StateMachineScriptParser.DefBranch2Context context)
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
            currentNode.EventDescriptions.Add(new NodeEventDescription()
            {
                Index = index,
                Description = event_name
            });
            base.ExitDefBranch2(context);
        }

        #endregion
    }
}
