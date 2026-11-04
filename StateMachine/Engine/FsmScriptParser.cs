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
        return parser.machine();
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
