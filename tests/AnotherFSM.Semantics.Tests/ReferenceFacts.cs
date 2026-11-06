namespace AnotherFSM.Semantics.Tests;

public sealed class ReferenceFacts
{
    [Fact]
    public void Reverse_references_are_factory_key_locations_including_implicit_keys_not_Node_or_Event_mentions()
    {
        var analysis = FsmAnalysis.Analyze(new("file.FSMScript", 9,
            "// Key\n def Query(Key) {Color:\"Key\";} def Key() {} def Key event; E -> Query to Key;"));
        var type = new TypeIdentity("P", "net8.0", "Nodes+Worker");
        var references = analysis.FindTypeReferences(new[] { new NodeType(type, "Key", true, false) }, type);
        Assert.Collection(references,
            explicitKey => { Assert.Equal(new SourceSpan(18, 3), explicitKey.Span); Assert.False(explicitKey.ImplicitKey); },
            implicitKey => { Assert.Equal(new SourceSpan(42, 3), implicitKey.Span); Assert.True(implicitKey.ImplicitKey); });
    }
}
