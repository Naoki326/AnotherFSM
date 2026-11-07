namespace AnotherFSM.Semantics;

/// <summary>Stable data, not a cached PSI element. Context is the consuming target framework.</summary>
public sealed record TypeIdentity(string Module, string Context, string ClrName, string Consumer = "", string Construction = "", string Alias = "global");
/// <summary>The host supplies only semantically verified FSMNode attributes, without instantiation.</summary>
public sealed record NodeType(TypeIdentity Identity, string? Key, bool HasNodeContract, bool IsOpenGeneric);
public enum BindingState { Determined, Unresolved, Ambiguous }
public sealed record NodeBinding(BindingState State, IReadOnlyList<TypeIdentity> Candidates, string Reason);
public enum KeyComparison { OrdinalIgnoreCase, Ordinal }
public sealed record ExplicitNodeMapping(string Key, IReadOnlyList<NodeType> Targets, string Source);
public sealed record BindingOptions(KeyComparison Comparison, IReadOnlyList<ExplicitNodeMapping> Mappings,
    string? Error = null)
{
    public static BindingOptions Default { get; } = new(KeyComparison.OrdinalIgnoreCase, Array.Empty<ExplicitNodeMapping>());
}

public static class NodeBindings
{
    public static NodeBinding Resolve(NodeDeclaration node, IEnumerable<NodeType> types, BindingOptions? options = null)
    {
        options ??= BindingOptions.Default;
        if (options.Error != null) return new(BindingState.Unresolved, Array.Empty<TypeIdentity>(), options.Error);
        var comparison = options.Comparison == KeyComparison.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var mappings = options.Mappings.Where(mapping => string.Equals(mapping.Key, node.TypeKey, comparison)).ToArray();
        if (mappings.Length > 0)
        {
            var targets = mappings.SelectMany(mapping => mapping.Targets).ToArray();
            var identities = targets.Select(target => target.Identity).Distinct().ToArray();
            var source = string.Join("; ", mappings.Select(mapping => mapping.Source));
            var valid = targets.Length == 1 && targets[0].HasNodeContract && !targets[0].IsOpenGeneric;
            var mappedState = mappings.Length > 1 || identities.Length > 1 ? BindingState.Ambiguous
                : valid ? BindingState.Determined : BindingState.Unresolved;
            return new(mappedState, Array.AsReadOnly(identities), "Explicit mapping: " + source + "; "
                + (mappedState == BindingState.Ambiguous ? "duplicate/conflicting explicit mappings; no first/last wins"
                    : !valid ? "target unavailable, ambiguous, open generic or node contract unconfirmed; default not substituted"
                    : "overrides FSMNode convention in this host context; not runtime registration proof"));
        }
        var matches = types.Where(t => !string.IsNullOrWhiteSpace(t.Key)
            && string.Equals(t.Key, node.TypeKey, comparison)).ToArray();
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
