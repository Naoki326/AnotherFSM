namespace AnotherFSM.Semantics.Tests;

public sealed class InterfaceReferenceFacts
{
    [Fact]
    public void Alias_chains_keep_local_declarations_but_normalize_Module_navigation_and_reference_scope()
    {
        const string text = "import M as A; import A as B; use B as X;";
        const string module = "module M { input Entry; def Entry(Key) {} }";
        var graph = SemanticGraph.Build(new[] { FsmAnalysis.Analyze(new("flow.fsm", 1, text)) }, (_, name) =>
            name == "M" ? FsmAnalysis.Analyze(new("M.fsm", 1, module)) : throw new InvalidOperationException("An existing alias is not a filename"));
        var source = graph.Navigate("flow.fsm", text.IndexOf("B as X", StringComparison.Ordinal), "moduleDefinition");
        Assert.Equal("M.fsm", Assert.Single(source.Locations).DocumentId);
        Assert.Equal(text.IndexOf("B;", StringComparison.Ordinal), Assert.Single(graph.Navigate("flow.fsm", text.IndexOf("B as X", StringComparison.Ordinal)).Locations).Span.Start);
        Assert.Equal(new[] { text.IndexOf("M as A", StringComparison.Ordinal), text.IndexOf("A as B", StringComparison.Ordinal), text.IndexOf("B as X", StringComparison.Ordinal) },
            graph.FindReferences("M.fsm", module.IndexOf("M {", StringComparison.Ordinal)).Locations.Select(location => location.Span.Start).Order().ToArray());
    }

    [Fact]
    public void Damaged_multi_Input_Module_cannot_become_an_implicit_single_entry()
    {
        const string text = """
            module M { input N,Missing; def N(Key) {} }
            use M as X;
            def Start(Key) {1->Go;}
            Go -> Start to X;
            """;
        var graph = SemanticGraph.Build(new[] { FsmAnalysis.Analyze(new("flow.fsm", 1, text)) }, (_, _) => null);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.LastIndexOf("X;", StringComparison.Ordinal)).Status);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf("X;", StringComparison.Ordinal), "entry").Status);
    }

    [Fact]
    public void Instance_query_can_switch_to_its_Module_template_references()
    {
        const string text = """
            module M { input Entry; def Entry(Key) {} }
            use M as Left;
            use M as Right;
            def Start(Key) {1->Go;}
            Go -> Start to Left;
            """;
        var graph = SemanticGraph.Build(new[] { FsmAnalysis.Analyze(new("flow.fsm", 1, text)) }, (_, _) => null);
        var references = graph.FindReferences("flow.fsm", text.LastIndexOf("Left", StringComparison.Ordinal), template: true);
        Assert.Equal(new[] { text.IndexOf("M as Left", StringComparison.Ordinal), text.IndexOf("M as Right", StringComparison.Ordinal) },
            references.Locations.Select(location => location.Span.Start).Order().ToArray());
    }

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
