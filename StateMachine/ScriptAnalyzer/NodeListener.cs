using Antlr4.Runtime.Misc;
using System.Globalization;

namespace StateMachine.ScriptAnalyzer
{

    internal class NodeListener : StateMachineScriptBaseListener
    {
        public event Action<IVisualNode>? StateCreated;

        internal HashSet<string> ModuleInstanceNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> NodeNames { get; } = new(StringComparer.Ordinal);
        private string nameprev = "";
        private int moduleDepth;

        public override void EnterModule_statement(StateMachineScriptParser.Module_statementContext context) => moduleDepth++;
        public override void ExitModule_statement(StateMachineScriptParser.Module_statementContext context) => moduleDepth--;

        public override void EnterModule_instance(StateMachineScriptParser.Module_instanceContext context)
        {
            if (moduleDepth > 0) return;
            foreach (var property in context.state_branch())
                if (property is not StateMachineScriptParser.PosDefContext
                    and not StateMachineScriptParser.ColorDefContext
                    and not StateMachineScriptParser.FlowIDDefContext)
                    throw new ScriptException($"模块实例 {nameprev + context.STRING()[1].GetText()} 的 use 属性只允许 Pos、Color、FlowID；请使用实例.Output -> Target;");
            currentNode = new VisualNode
            {
                Name = nameprev + context.STRING()[1].GetText(),
                NamePrefix = nameprev,
                ClassType = context.STRING()[0].GetText(),
                Color = "white",
            };
            ModuleInstanceNames.Add(currentNode.Name);
        }

        public override void ExitModule_instance(StateMachineScriptParser.Module_instanceContext context)
        {
            if (moduleDepth > 0) return;
            StateCreated?.Invoke(currentNode);
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

        #region DefNode

        private IVisualNode currentNode = null!;

        public override void EnterDefState([NotNull] StateMachineScriptParser.DefStateContext context)
        {
            if (moduleDepth > 0) return;
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
            if (moduleDepth > 0) return;
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
            if (moduleDepth > 0) return;
            NodeNames.Add(currentNode.Name);
            StateCreated?.Invoke(currentNode);
            base.ExitDefState(context);
        }

        public override void ExitDefState2([NotNull] StateMachineScriptParser.DefState2Context context)
        {
            if (moduleDepth > 0) return;
            NodeNames.Add(currentNode.Name);
            StateCreated?.Invoke(currentNode);
            base.ExitDefState2(context);
        }

        #endregion

        #region Node Property

        public override void ExitColorDef([NotNull] StateMachineScriptParser.ColorDefContext context)
        {
            if (moduleDepth > 0) return;
            currentNode.Color = context.CODESTRING().GetText().Trim('"');
            base.ExitColorDef(context);
        }

        public override void ExitPosDef([NotNull] StateMachineScriptParser.PosDefContext context)
        {
            if (moduleDepth > 0) return;
            string posxStr = context.position().posx().GetText();
            string posyStr = context.position().posy().GetText();
            currentNode.PosX = double.Parse(posxStr, CultureInfo.InvariantCulture);
            currentNode.PosY = double.Parse(posyStr, CultureInfo.InvariantCulture);
            base.ExitPosDef(context);
        }

        public override void ExitFlowIDDef([NotNull] StateMachineScriptParser.FlowIDDefContext context)
        {
            if (moduleDepth > 0) return;
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
            if (moduleDepth > 0) return;
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
            if (moduleDepth > 0) return;
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
