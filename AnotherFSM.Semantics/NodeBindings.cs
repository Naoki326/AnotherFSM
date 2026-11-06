namespace AnotherFSM.Semantics;

/// <summary>Stable data, not a cached PSI element. Context is the consuming target framework.</summary>
public sealed record TypeIdentity(string Module, string Context, string ClrName);
/// <summary>The host supplies only semantically verified FSMNode attributes, without instantiation.</summary>
public sealed record NodeType(TypeIdentity Identity, string? Key, bool HasNodeContract, bool IsOpenGeneric);
public enum BindingState { Determined, Unresolved, Ambiguous }
public sealed record NodeBinding(BindingState State, IReadOnlyList<TypeIdentity> Candidates, string Reason);

public static class NodeBindings
{
    public static NodeBinding Resolve(NodeDeclaration node, IEnumerable<NodeType> types)
    {
        var matches = types.Where(t => !string.IsNullOrWhiteSpace(t.Key)
            && string.Equals(t.Key, node.TypeKey, StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidates = matches.Select(t => t.Identity).Distinct().ToArray();
        var invalid = matches.Any(t => !t.HasNodeContract || t.IsOpenGeneric);
        var state = candidates.Length > 1 ? BindingState.Ambiguous
            : candidates.Length == 1 && !invalid ? BindingState.Determined : BindingState.Unresolved;
        var reason = state == BindingState.Ambiguous
            ? "Conflicting FSMNode attribute keys: " + string.Join(", ", candidates.Select(t => t.Module + "/" + t.Context + ":" + t.ClrName))
            : invalid ? "Node contract unconfirmed or open generic; not a concrete Node binding"
            : candidates.Length == 0 ? "No verified FSMNode attribute key in this host context"
            : "FSMNode attribute convention; not proof of runtime registration";
        return new NodeBinding(state, Array.AsReadOnly(candidates), reason);
    }
}
