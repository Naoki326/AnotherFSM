namespace AnotherFSM.Semantics.Tests;

public sealed class BindingFacts
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Unconfirmed_node_contract_or_open_generic_never_proves_a_binding(bool contract, bool generic)
    {
        var node = FsmAnalysis.Analyze(new("flow.fsm", 1, "def Query(Key) {}")).Nodes.Single();
        var binding = NodeBindings.Resolve(node, new[] { new NodeType(new("P", "net8.0", "A"), "Key", contract, generic) });
        Assert.Equal(BindingState.Unresolved, binding.State);
        Assert.NotEmpty(binding.Reason);
    }

    [Fact]
    public void Conflicting_full_type_identities_never_pick_the_first_type()
    {
        var node = FsmAnalysis.Analyze(new("flow.fsm", 1, "def Query(Key) {}")).Nodes.Single();
        var binding = NodeBindings.Resolve(node, new[] {
            new NodeType(new("P", "net8.0", "A"), "Key", true, false),
            new NodeType(new("P", "net8.0", "B"), "KEY", true, false) });
        Assert.Equal(BindingState.Ambiguous, binding.State);
        Assert.Equal(2, binding.Candidates.Count);
    }

    [Fact]
    public void Factory_key_binds_full_type_identity_case_insensitively_not_Node_or_class_name()
    {
        var analysis = FsmAnalysis.Analyze(new("flow.fsm", 7, "def Query(businessquery) {}"));
        var types = new[] { new NodeType(new TypeIdentity("project-A", "net8.0", "Business.Nodes+RealNode"), "BusinessQuery", true, false) };
        var binding = NodeBindings.Resolve(analysis.Nodes.Single(), types);
        Assert.Equal(BindingState.Determined, binding.State);
        Assert.Equal("Business.Nodes+RealNode", binding.Candidates.Single().ClrName);
    }
}
