using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using AnotherFSM.Semantics.Syntax;

namespace AnotherFSM.Semantics;

/// <summary>Static editor projection of the authoritative grammar; never constructs a business Node.</summary>
public static class FsmAnalysis
{
    public static DocumentAnalysis Analyze(DocumentSnapshot document, CancellationToken cancellation = default, Action? checkHostCancellation = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var errors = new Errors(document.Text);
        var lexer = new CancellableLexer(document.Text, cancellation, checkHostCancellation);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errors);
        var parser = new StateMachineScriptParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errors);
        var tree = parser.machine();
        cancellation.ThrowIfCancellationRequested();
        var projection = new Projection(cancellation, errors.Offsets);
        projection.Visit(tree);
        return new DocumentAnalysis(document, projection.Nodes.Where(node => node.Scope != "" || !projection.BlockedNames.Contains(node.Name))
            .Select(node => node with { DocumentId = document.Id }), projection.References, errors.Messages, tree, errors.Offsets,
            projection.Nodes.Select(node => node with { DocumentId = document.Id }));
    }

    private sealed class CancellableLexer(string text, CancellationToken cancellation, Action? checkHostCancellation)
        : StateMachineScriptLexer(new AntlrInputStream(text))
    {
        public override IToken NextToken()
        {
            cancellation.ThrowIfCancellationRequested();
            checkHostCancellation?.Invoke();
            return base.NextToken();
        }
    }

    private sealed class Errors(string text) : BaseErrorListener, IAntlrErrorListener<int>
    {
        public List<string> Messages { get; } = new();
        public List<int> Offsets { get; } = new();
        private void Add(int line, int column, string message)
        {
            var start = 0;
            for (var current = 1; current < line; current++)
            {
                var next = text.IndexOf('\n', start);
                if (next < 0) break;
                start = next + 1;
            }
            Offsets.Add(start + column);
            Messages.Add($"{line}:{column}: {message}");
        }
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
            => Add(line, charPositionInLine, msg);
        void IAntlrErrorListener<int>.SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
            => Add(line, charPositionInLine, msg);
    }

    private sealed class Projection : StateMachineScriptBaseVisitor<object?>
    {
        private readonly CancellationToken cancellation;
        private string prefix = "";
        private string scope = "";
        public readonly List<NodeDeclaration> Nodes = new();
        public readonly List<NodeReference> References = new();
        public readonly HashSet<string> BlockedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<int> errorOffsets;
        public Projection(CancellationToken cancellation, IReadOnlyList<int> errorOffsets)
            => (this.cancellation, this.errorOffsets) = (cancellation, errorOffsets);
        private bool HasError(int start, int end) => errorOffsets.Any(p => p >= start && p <= end);
        public override object? VisitModule_statement(StateMachineScriptParser.Module_statementContext context)
        {
            var open = context.children.OfType<ITerminalNode>().FirstOrDefault(n => n.GetText() == "{");
            if (open == null || open is IErrorNode || open.Symbol.TokenIndex < 0
                || HasError(context.Start.StartIndex, open.Symbol.StartIndex)) return null;
            var saved = scope;
            scope = "module:" + context.Start.StartIndex;
            Visit(context.module_body());
            scope = saved;
            return null;
        }
        public override object? VisitModule_instance(StateMachineScriptParser.Module_instanceContext context)
        {
            var names = context.STRING();
            if (names.Length == 2 && names[1].Symbol.TokenIndex >= 0)
                BlockedNames.Add(AnotherFSM.Syntax.DeclarationRules.Qualify(prefix, names[1].GetText()));
            return null;
        }
        public override object? VisitNamespace(StateMachineScriptParser.NamespaceContext context)
        {
            var open = context.children.OfType<ITerminalNode>().FirstOrDefault(n => n.GetText() == "{");
            if (open == null || open is IErrorNode || open.Symbol.TokenIndex < 0
                || context.STRING() is IErrorNode || context.STRING().Symbol.TokenIndex < 0
                || HasError(context.Start.StartIndex, open.Symbol.StartIndex)) return null;
            var saved = prefix;
            prefix = context.STRING().GetText() + ".";
            foreach (var expression in context.expression()) Visit(expression);
            prefix = saved;
            return null;
        }
        public override object? VisitDefState(StateMachineScriptParser.DefStateContext context) => Define(context, context.STRING());
        public override object? VisitDefState2(StateMachineScriptParser.DefState2Context context) => Define(context, context.STRING());
        private object? Define(ParserRuleContext context, ITerminalNode[] names)
        {
            cancellation.ThrowIfCancellationRequested();
            var header = context.children.OfType<ITerminalNode>().TakeWhile(n => n.GetText() != "{").ToArray();
            var open = context.children.OfType<ITerminalNode>().FirstOrDefault(n => n.GetText() == "{");
            // No missing/synthetic/recovered token can prove a complete declaration header.
            if (open == null || open is IErrorNode || open.Symbol.TokenIndex < 0 || names.Length == 0
                || header.Any(n => n is IErrorNode || n.Symbol.TokenIndex < 0)
                || HasError(context.Start.StartIndex, open.Symbol.StartIndex)) return null;
            var name = names[0];
            var key = names[^1];
            var typeKey = AnotherFSM.Syntax.DeclarationRules.TypeKey(name.GetText(), names.Length == 1 ? null : key.GetText());
            if (AnotherFSM.Syntax.DeclarationRules.IsRemovedComposition(typeKey)) return null;
            Nodes.Add(new NodeDeclaration(AnotherFSM.Syntax.DeclarationRules.Qualify(prefix, name.GetText()), typeKey,
                Span(name), Span(key), names.Length == 1, scope));
            return null;
        }
        public override object? VisitDefTransition(StateMachineScriptParser.DefTransitionContext context)
        {
            cancellation.ThrowIfCancellationRequested();
            var names = context.STRING();
            if (names.Length == 3 && !HasError(context.Start.StartIndex, context.Stop.StopIndex)
                && !context.children.OfType<ITerminalNode>().Any(n => n is IErrorNode || n.Symbol.TokenIndex < 0))
                foreach (var name in names.Skip(1)) References.Add(new NodeReference(prefix + name.GetText(), Span(name), scope));
            return null;
        }
        private static SourceSpan Span(ITerminalNode token) => new(token.Symbol.StartIndex, token.Symbol.StopIndex - token.Symbol.StartIndex + 1);
    }
}
