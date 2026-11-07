namespace AnotherFSM.Semantics.Tests;

public sealed class OverrideBindingFacts
{
    [Fact]
    public void Native_reverse_query_uses_the_same_explicit_override_instead_of_the_default_attribute()
    {
        var document = FsmAnalysis.Analyze(new("flow.fsm", 1, "def Query(Key) {}"));
        var original = new NodeType(new("default", "net8.0", "A"), "Key", true, false);
        var mapped = new NodeType(new("dependency", "net8.0", "B"), null, true, false);
        var options = new BindingOptions(KeyComparison.OrdinalIgnoreCase,
            new[] { new ExplicitNodeMapping("Key", new[] { mapped }, "anotherfsm.json") });
        Assert.Empty(document.FindTypeReferences(new[] { original }, original.Identity, options));
        Assert.Equal("Key", document.FindTypeReferences(new[] { original }, mapped.Identity, options).Single().TypeKey);
    }

    [Theory]
    [InlineData(false, BindingState.Unresolved)]
    [InlineData(true, BindingState.Ambiguous)]
    public void Invalid_or_duplicate_explicit_mappings_never_fall_back_to_a_valid_default(bool duplicate, BindingState expected)
    {
        var node = FsmAnalysis.Analyze(new("flow.fsm", 1, "def Query(Key) {}")).Nodes.Single();
        var convention = new NodeType(new("default", "net8.0", "A"), "Key", true, false);
        var mapping = new ExplicitNodeMapping("Key", Array.Empty<NodeType>(), "anotherfsm.json");
        var options = new BindingOptions(KeyComparison.OrdinalIgnoreCase, duplicate ? new[] { mapping, mapping } : new[] { mapping });
        Assert.Equal(expected, NodeBindings.Resolve(node, new[] { convention }, options).State);
    }

    [Fact]
    public void Case_sensitive_host_keys_do_not_change_Node_names_or_other_host_defaults()
    {
        var node = FsmAnalysis.Analyze(new("flow.fsm", 1, "def Query(key) {}")).Nodes.Single();
        var type = new NodeType(new("P", "net8.0", "A"), "KEY", true, false);
        Assert.Equal(BindingState.Unresolved, NodeBindings.Resolve(node, new[] { type },
            new(KeyComparison.Ordinal, Array.Empty<ExplicitNodeMapping>())).State);
        Assert.Equal(BindingState.Determined, NodeBindings.Resolve(node, new[] { type }).State);
    }

    [Fact]
    public void Explicit_mapping_replaces_only_the_matching_default_key_and_reports_its_source()
    {
        var node = FsmAnalysis.Analyze(new("flow.fsm", 1, "def Query(Key) {}")).Nodes.Single();
        var convention = new NodeType(new("default", "net8.0", "A"), "Key", true, false);
        var mapped = new NodeType(new("dependency", "net8.0", "B"), null, true, false);
        var options = new BindingOptions(KeyComparison.OrdinalIgnoreCase,
            new[] { new ExplicitNodeMapping("KEY", new[] { mapped }, "anotherfsm.json: mappings[0]") });

        var binding = NodeBindings.Resolve(node, new[] { convention }, options);

        Assert.Equal(BindingState.Determined, binding.State);
        Assert.Equal(mapped.Identity, binding.Candidates.Single());
        Assert.Contains("anotherfsm.json", binding.Reason);
    }
}
