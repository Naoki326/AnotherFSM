using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace StateMachine.ScriptAnalyzer
{
    public static class StateMachineScriptAnalyzer
    {
        public static void ReadScript(this string input,
            Action<IVisualNode> nodeCreateAction,
            Action<string, string, string> connectAction)
        {
            var stream = new AntlrInputStream(input);
            var lexer = new StateMachineScriptLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new StateMachineScriptParser(tokens);
            var tree = parser.machine();

            var nodeListener = new NodeListener();
            var connectListener = new ConnectLineListener();

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
        public event Action<string, string, string> LineConnected;

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

        public override void ExitDefTransition([NotNull] StateMachineScriptParser.DefTransitionContext context)
        {
            var event_name = nameprev + context.STRING()[0].GetText();
            var sourceState_name = nameprev + context.STRING()[1].GetText();
            var targetState_name = nameprev + context.STRING()[2].GetText();

            LineConnected?.Invoke(event_name, sourceState_name, targetState_name);

            base.ExitDefTransition(context);
        }

    }
}
