namespace AnotherFSM.Semantics;

/// <summary>Offsets and lengths are UTF-16 code units in this exact document version.</summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    public bool Contains(int offset) => offset >= Start && offset < Start + Length;
}

public sealed record DocumentSnapshot(string Id, long Version, string Text);
public sealed record NodeDeclaration(string Name, string TypeKey, SourceSpan NameSpan, SourceSpan KeySpan, bool ImplicitKey, string Scope = "", string DocumentId = "");
public sealed record NodeReference(string Name, SourceSpan Span, string Scope = "");
public sealed record TypeReference(SourceSpan Span, string TypeKey, bool ImplicitKey);

public sealed class DocumentAnalysis
{
    internal Syntax.StateMachineScriptParser.MachineContext? SyntaxTree { get; }
    internal IReadOnlyList<int> ErrorOffsets { get; }
    internal IReadOnlyList<NodeDeclaration> SyntaxNodes { get; }
    public DocumentSnapshot Document { get; }
    public IReadOnlyList<NodeDeclaration> Nodes { get; }
    public IReadOnlyList<NodeReference> References { get; }
    public IReadOnlyList<string> Diagnostics { get; }

    internal DocumentAnalysis(DocumentSnapshot document, IEnumerable<NodeDeclaration> nodes,
        IEnumerable<NodeReference> references, IEnumerable<string> diagnostics,
        Syntax.StateMachineScriptParser.MachineContext? syntaxTree = null, IReadOnlyList<int>? errorOffsets = null,
        IEnumerable<NodeDeclaration>? syntaxNodes = null)
    {
        SyntaxTree = syntaxTree;
        ErrorOffsets = errorOffsets ?? Array.Empty<int>();
        Document = document;
        Nodes = Array.AsReadOnly(nodes.ToArray());
        SyntaxNodes = Array.AsReadOnly((syntaxNodes ?? Nodes).ToArray());
        References = Array.AsReadOnly(references.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public NodeDeclaration? FindFactoryDeclaration(int offset)
    {
        var node = Nodes.FirstOrDefault(n => n.KeySpan.Contains(offset));
        return node != null && Nodes.Count(n => n.Name == node.Name && n.Scope == node.Scope) == 1 ? node : null;
    }

    public IReadOnlyList<TypeReference> FindTypeReferences(IEnumerable<NodeType> types, TypeIdentity target, BindingOptions? options = null)
    {
        var catalog = types.ToArray();
        return Array.AsReadOnly(Nodes.Where(node => Nodes.Count(n => n.Name == node.Name && n.Scope == node.Scope) == 1)
            .Where(node => { var binding = NodeBindings.Resolve(node, catalog, options);
                return binding.State == BindingState.Determined && binding.Candidates[0] == target; })
            .Select(node => new TypeReference(node.KeySpan, node.TypeKey, node.ImplicitKey)).Distinct().ToArray());
    }

    public NodeDeclaration? FindNodeDeclaration(int offset)
    {
        var node = Nodes.FirstOrDefault(n => n.NameSpan.Contains(offset));
        var reference = References.FirstOrDefault(r => r.Span.Contains(offset));
        var name = node?.Name ?? reference?.Name;
        var scope = node?.Scope ?? reference?.Scope;
        if (name == null) return null;
        var declarations = Nodes.Where(n => n.Name == name && n.Scope == scope).ToArray();
        return declarations.Length == 1 ? declarations[0] : null;
    }
}
