using Antlr4.Runtime;

namespace StateMachine;

internal static class FsmScriptParser
{
    internal static StateMachineScriptParser.MachineContext Parse(string input)
    {
        var lexer = new StateMachineScriptLexer(new AntlrInputStream(input));
        var errors = new ScriptErrors();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errors);
        var tokens = new CommonTokenStream(lexer);
        var parser = new StateMachineScriptParser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errors);
        var tree = parser.machine();
        // Validate rejection-only syntax even for Transform, which visits transitions only.
        new UnsupportedComposition().Visit(tree);
        return tree;
    }

    private sealed class UnsupportedComposition : StateMachineScriptBaseVisitor<object>
    {
        public override object? VisitDefState(StateMachineScriptParser.DefStateContext context)
        {
            CheckType(context.STRING().Last().GetText(), context.Start);
            return base.VisitDefState(context);
        }

        public override object? VisitDefState2(StateMachineScriptParser.DefState2Context context)
        {
            CheckType(context.STRING().Last().GetText(), context.Start);
            return base.VisitDefState2(context);
        }

        private static void CheckType(string type, IToken start)
        {
            try { RemovedComposition.RejectType(type); }
            catch (ScriptException ex) { throw new ScriptException($"{ex.Message} At line {start.Line}, column {start.Column + 1}."); }
        }

        public override object? VisitRemovedCompositionDef(StateMachineScriptParser.RemovedCompositionDefContext context)
            => throw new ScriptException($"{RemovedComposition.Diagnostic} Unsupported structure {context.GetText()} at line {context.Start.Line}, column {context.Start.Column + 1}.");
    }

    private sealed class ScriptErrors : BaseErrorListener, IAntlrErrorListener<int>
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
            => throw Error(line, charPositionInLine, msg);

        void IAntlrErrorListener<int>.SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
            => throw Error(line, charPositionInLine, msg);

        private static ScriptException Error(int line, int column, string message)
            => new($"脚本语法错误 ({line}:{column}): {message}。模块请使用 use Module as Instance; 与 Instance.Output -> Target;，不支持旧 terminal/output 映射。");
    }
}
