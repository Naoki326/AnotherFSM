using StateMachine;
using StateMachine.ScriptAnalyzer;

namespace StateMachineTest;

public class ScriptAnalysisFacts
{
    [Theory]
    [InlineData("OK -> Alias;")]
    [InlineData("1 -> Alias;")]
    [InlineData("Type:Step;")]
    public void ReadScript_DoesNotSilentlyIgnoreInvalidModuleInstanceProperties(string property)
    {
        Action analyze = () => $"use Checker as Check {{ {property} }}".ReadScript(_ => { }, (_, _, _) => { });
        analyze.Should().Throw<ScriptException>().WithMessage("*Check*Pos*Color*FlowID*");
    }

    [Fact]
    public void ReadScript_DoesNotTreatOrdinaryDottedNodeNamesAsModuleMembers()
    {
        const string script = """
            use Checker as Global;
            namespace Flow {
                def Global.Start(Step) { 1 -> Audit.Ready; }
                def End(Step) { }
                Audit.Ready -> Global.Start to End;
            }
            """;
        var connections = new List<(string Event, string Source, string Target)>();
        script.ReadScript(_ => { }, (ev, source, target) => connections.Add((ev, source, target)));

        connections.Should().Equal(("Flow.Audit.Ready", "Flow.Global.Start", "Flow.End"));
    }

    [Fact]
    public void ReadScript_PreservesImportedModuleAliasesAndFullyQualifiedInstanceReferences()
    {
        const string script = """
            import External as Checker;
            use Checker as Global;
            namespace Flow {
                def Start(Step) { 1 -> Begin; }
                use Checker as Local;
                Begin -> Start to Global;
                Global.OK -> Local.Retry;
                Flow.Local.OK -> Global;
            }
            """;
        var nodes = new List<IVisualNode>();
        var connections = new List<(string Event, string Source, string Target)>();
        script.ReadScript(nodes.Add, (ev, source, target) => connections.Add((ev, source, target)));

        nodes.Where(n => n.ClassType == "Checker").Select(n => n.Name)
            .Should().BeEquivalentTo("Global", "Flow.Local");
        connections.Should().BeEquivalentTo(new[]
        {
            ("Flow.Begin", "Flow.Start", "Global"),
            ("Global.OK", "Global", "Flow.Local.Retry"),
            ("Flow.Local.OK", "Flow.Local", "Global"),
        });
    }

    [Fact]
    public void ReadScript_ResolvesDottedOutputsByDeclaredPublicIdentityNotLongestPrefix()
    {
        const string script = """
            module Outer { input Entry; output Sub.Done; def Entry(Step) { 1->Sub.Done; } }
            module Other { input Entry; output OK; def Entry(Step) { 1->OK; } }
            use Outer as Check;
            use Other as Check.Sub;
            def End(Step) {}
            Check.Sub.Done -> End;
            """;
        var engine = FSMEngineBuilder.Create().ConfigureNodeFactory(new TestInfrastructure.ScriptNodeFactory()).Build();
        engine.CreateStateMachine(script);
        engine.GetNode("Check.Entry").TargetState(engine.GetEvent("Check.Sub.Done")).Name.Should().Be("End");
        var connections = new List<(string Event, string Source, string Target)>();

        script.ReadScript(_ => { }, (ev, source, target) => connections.Add((ev, source, target)));

        connections.Should().Equal(("Check.Sub.Done", "Check", "End"));
    }

    [Fact]
    public void ReadScript_FullyQualifiedOutputIsNotShadowedByAnUnrelatedLocalPrefix()
    {
        const string script = """
            module Outer { input Entry; output Done; def Entry(Step) { 1->Done; } }
            module Other { input Entry; output OK; def Entry(Step) { 1->OK; } }
            use Outer as Area.Check;
            namespace N {
                use Other as Area;
                def End(Step) {}
                Area.Check.Done -> End;
            }
            """;
        var engine = FSMEngineBuilder.Create().ConfigureNodeFactory(new TestInfrastructure.ScriptNodeFactory()).Build();
        engine.CreateStateMachine(script);
        engine.GetNode("Area.Check.Entry").TargetState(engine.GetEvent("Area.Check.Done")).Name.Should().Be("N.End");
        var connections = new List<(string Event, string Source, string Target)>();

        script.ReadScript(_ => { }, (ev, source, target) => connections.Add((ev, source, target)));

        connections.Should().Equal(("Area.Check.Done", "Area.Check", "N.End"));
    }

    [Fact]
    public void ReadScript_RejectsAmbiguousExternalOutputOwnershipInsteadOfGuessing()
    {
        const string script = """
            import External as Outer;
            import Another as Inner;
            use Outer as Check;
            use Inner as Check.Sub;
            def End(Step) {}
            Check.Sub.Done -> End;
            """;

        Action analyze = () => script.ReadScript(_ => { }, (_, _, _) => { });

        analyze.Should().Throw<ScriptException>().WithMessage("*Check.Sub.Done*多个*FSMEngine*");
    }

    [Fact]
    public void ReadScript_PreservesOrdinaryNodesDottedEventsAndBranchBindings()
    {
        const string script = """
            namespace Ordinary {
                def Start as Step state {
                    branch 0 -> Audit.Ready;
                    success -> Done;
                    Pos:(1.25,-2.5); Color:"blue"; FlowID:42;
                }
                def End(Step) { }
                Audit.Ready -> Start to End;
                Done -> Start to End;
            }
            """;
        var nodes = new List<IVisualNode>();
        var connections = new List<(string Event, string Source, string Target)>();
        script.ReadScript(nodes.Add, (ev, source, target) => connections.Add((ev, source, target)));

        var start = nodes.Single(n => n.Name == "Ordinary.Start");
        start.ClassType.Should().Be("Step");
        start.EventDescriptions.Select(e => (e.Index, e.Description)).Should().BeEquivalentTo(new[]
        {
            (0, "Ordinary.Audit.Ready"), (1, "Ordinary.Done"),
        });
        start.PosX.Should().Be(1.25);
        start.PosY.Should().Be(-2.5);
        start.FlowID.Should().Be("42");
        connections.Should().BeEquivalentTo(new[]
        {
            ("Ordinary.Audit.Ready", "Ordinary.Start", "Ordinary.End"),
            ("Ordinary.Done", "Ordinary.Start", "Ordinary.End"),
        });
    }

    [Fact]
    public void ReadScript_ReportsModuleInstancesAndPublicConnectionsWithoutTemplateNodes()
    {
        const string script = """
            module Checker {
                input Entry, Retry;
                output OK;
                def Entry(Step) { success -> OK; }
                def Retry(Step) { 1 -> OK; }
            }
            namespace Flow {
                def Start(Step) { 1 -> Launch; }
                use Checker as CheckA { Pos:(12.5,-4); Color:"red"; FlowID:saved; }
                use Checker as CheckB;
                def End(Step) { }
                Launch -> Start to CheckA.Retry;
                CheckA.OK -> CheckB.Entry;
                CheckB.OK -> End;
            }
            """;
        var nodes = new List<IVisualNode>();
        var connections = new List<(string Event, string Source, string Target)>();

        script.ReadScript(nodes.Add, (ev, source, target) => connections.Add((ev, source, target)));

        nodes.Select(n => n.Name).Should().BeEquivalentTo("Flow.Start", "Flow.CheckA", "Flow.CheckB", "Flow.End");
        var instance = nodes.Single(n => n.Name == "Flow.CheckA");
        instance.ClassType.Should().Be("Checker");
        instance.NamePrefix.Should().Be("Flow.");
        instance.PosX.Should().Be(12.5);
        instance.PosY.Should().Be(-4);
        instance.Color.Should().Be("red");
        instance.FlowID.Should().Be("saved");
        connections.Should().BeEquivalentTo(new[]
        {
            ("Flow.Launch", "Flow.Start", "Flow.CheckA.Retry"),
            ("Flow.CheckA.OK", "Flow.CheckA", "Flow.CheckB.Entry"),
            ("Flow.CheckB.OK", "Flow.CheckB", "Flow.End"),
        });
    }
}
