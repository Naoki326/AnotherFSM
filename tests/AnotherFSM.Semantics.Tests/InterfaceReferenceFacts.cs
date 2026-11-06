namespace AnotherFSM.Semantics.Tests;

public sealed class InterfaceReferenceFacts
{
    [Fact]
    public void Entry_operation_does_not_report_a_Module_header_as_an_instance_entry()
    {
        const string text = "module Broken { input Missing; } use Broken as Damaged;";
        var graph = SemanticGraph.Build(new[] { FsmAnalysis.Analyze(new("flow.fsm", 1, text)) }, (_, _) => null);
        var navigation = graph.Navigate("flow.fsm", text.LastIndexOf("Broken", StringComparison.Ordinal), "entry");
        Assert.Equal("unresolved", navigation.Status);
        Assert.Empty(navigation.Locations);
    }

    [Fact]
    public void Omitted_single_Input_is_a_real_interface_reference_without_becoming_an_instance_Node()
    {
        const string text = """
            module Checker { input Entry; def Entry(Key) {} }
            def Start(Key) {1->A;2->B;}
            use Checker as Left;
            A -> Start to Left;
            B -> Start to Left.Entry;
            """;
        var graph = SemanticGraph.Build(new[] { FsmAnalysis.Analyze(new("flow.fsm", 1, text)) }, (_, _) => null);
        var references = graph.FindReferences("flow.fsm", text.LastIndexOf("Entry", StringComparison.Ordinal));
        var omitted = text.IndexOf("to Left;", StringComparison.Ordinal) + 3;
        Assert.Equal(new[] { omitted, text.LastIndexOf("Entry", StringComparison.Ordinal) },
            references.Locations.Select(location => location.Span.Start).Order().ToArray());
        Assert.Null(graph.FindNodeSource("flow.fsm", omitted));
    }
}
