using AnotherFSM.Semantics;

namespace AnotherFSM.Semantics.Tests;

public sealed class NavigationFacts
{
    [Theory]
    [InlineData("def Query(BusinessQuery) {1->", 1)]
    [InlineData("def Query(BusinessQuery {", 0)]
    [InlineData("def Query(BusinessQue", 0)]
    [InlineData("// def Fake(BusinessQuery) {}\n def Query() {}", 1)]
    [InlineData("def Query(Group) {}", 0)]
    [InlineData("def Query(Key@) {}", 0)]
    [InlineData("namespace Work@ { def Query(Key) {} }", 0)]
    [InlineData("// def Fake(Key) {}", 0)]
    public void Only_complete_legal_headers_establish_Node_identity(string text, int expected)
    {
        var result = FsmAnalysis.Analyze(new DocumentSnapshot("flow.fsm", 1, text));
        Assert.Equal(expected, result.Nodes.Count);
    }

    [Fact]
    public void Source_positions_remain_UTF16_after_a_non_BMP_comment()
    {
        var result = FsmAnalysis.Analyze(new("flow.fsm", 1, "// 🧭\ndef A(Key) {} E -> A to A;"));
        var node = result.FindNodeDeclaration(25);
        Assert.NotNull(node);
        Assert.Equal(new SourceSpan(10, 1), node.NameSpan);
        Assert.Equal(new SourceSpan(12, 3), node.KeySpan);
    }

    [Fact]
    public void Namespace_qualifies_Node_names_but_never_the_implicit_factory_key()
    {
        var result = FsmAnalysis.Analyze(new("unicode.fsm", 1, "namespace 工作 { def 查询() {} def 终点(K) {} E -> 查询 to 终点; }"));
        var node = result.FindNodeDeclaration(45);
        Assert.NotNull(node);
        Assert.Equal("工作.查询", node.Name);
        Assert.Equal("查询", node.TypeKey);
        Assert.True(node.ImplicitKey);
    }

    [Fact]
    public void Unsupported_Module_Instance_does_not_become_an_ordinary_Node_target()
    {
        var result = FsmAnalysis.Analyze(new("flow.fsm", 1, "def A(K) {} use Missing as a; E -> A to A;"));
        Assert.Null(result.FindNodeDeclaration(34));
        Assert.Null(result.FindFactoryDeclaration(6));
    }

    [Fact]
    public void Duplicate_Node_declarations_do_not_pick_a_navigation_target()
    {
        var result = FsmAnalysis.Analyze(new("flow.fsm", 1, "def A(K) {} def A(K) {} E -> A to A;"));
        Assert.Null(result.FindNodeDeclaration(29));
        Assert.Null(result.FindFactoryDeclaration(6));
    }

    [Fact]
    public void Node_reference_navigates_to_its_DSL_declaration_not_factory_key()
    {
        var result = FsmAnalysis.Analyze(new DocumentSnapshot("flow.fsm", 1,
            "def Query(BusinessQuery) {} def End(Stop) {} Next -> Query to End;"));
        var target = result.FindNodeDeclaration(53);
        Assert.NotNull(target);
        Assert.Equal("Query", target.Name);
        Assert.Equal("BusinessQuery", target.TypeKey);
        Assert.Equal(new SourceSpan(4, 5), target.NameSpan);
        Assert.Equal(new SourceSpan(10, 13), target.KeySpan);
    }
}
