using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace StateMachineTest;

public class ModuleDslFacts
{
    private const string Checker = """
        module Checker {
            input Entry;
            output Done, Failed;
            def Entry(Step) { 1->Next; 3->Failed; }
            def Exit(Step) { 1->Done; }
            Next -> Entry to Exit;
        }
        """;

    [Theory]
    [InlineData("def Start(Step) { } @")]
    [InlineData("def Start(Step) { } Launch -> Start to Start")]
    [InlineData("module Broken { input Entry; def Entry(Step) {} terminal Entry; }")]
    public void InvalidSyntax_FailsAtPublicBuildAndTryBoundaries(string script)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action build = () => engine.CreateStateMachine(script);
        build.Should().Throw<ScriptException>();
        engine.TryCreateStateMachine(script).Should().BeFalse();
    }

    [Theory]
    [InlineData("use Checker as Check; use Checker as Check;")]
    [InlineData("def Check(Step) {} use Checker as Check;")]
    [InlineData("use Checker as Check; def Check(Step) {}")]
    [InlineData("def Check.Exit(Step) {} use Checker as Check;")]
    [InlineData("def Check.Done event; use Checker as Check;")]
    [InlineData("def Check.Next event; use Checker as Check;")]
    [InlineData("use Checker as Check; def Check.Done event;")]
    [InlineData("use Checker as Check; def Outside(Step) { 1->Check.Done; }")]
    [InlineData("use Checker as Check; use Checker as Check.Entry;")]
    public void ActualNameCollisions_FailWithoutMergingObjects(string script)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action build = () => engine.CreateStateMachine(Checker + script);
        build.Should().Throw<ScriptException>().WithMessage("*碰撞*");
    }

    [Theory]
    [InlineData("Entry, Retry", "Check", false)]
    [InlineData("", "Check", false)]
    [InlineData("Entry", "Check.Missing", false)]
    [InlineData("Entry", "Check.Private", false)]
    [InlineData("Entry, Retry", "Check.Retry", true)]
    [InlineData("Entry", "Check.Entry", true)]
    public void ModuleTargets_RequireAnUnambiguousPublicInput(string inputs, string target, bool valid)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        var script = $$"""
            module Choice {
                {{(inputs.Length > 0 ? "input " + inputs + ";" : "")}}
                def Entry(Step) {} def Retry(Step) {} def Private(Step) {}
            }
            use Choice as Check;
            def Start(Step) { 1->Launch; }
            Launch -> Start to {{target}};
            """;
        if (!valid)
        {
            Action build = () => engine.CreateStateMachine(script);
            build.Should().Throw<ScriptException>().WithMessage("*Check*input*");
            engine.TryCreateStateMachine(script).Should().BeFalse();
            return;
        }
        engine.CreateStateMachine(script);
        engine.GetNode("Start").TargetState(engine.GetEvent("Launch"))
            .Should().BeSameAs(engine.GetNode(target));
    }

    [Fact]
    public void MultiplePublishers_ConnectOncePerNodeWithoutChangingBranchBindings()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine("""
            module Choice {
                input Entry; output Done, Unused;
                def Entry(Step) { 1->Next; 2->Done; 3->Done; }
                def Middle(Step) { 1->Done; }
                Next -> Entry to Middle;
            }
            use Choice as Check;
            def End(Step) {}
            Check.Done -> End;
            """);
        foreach (var name in new[] { "Check.Entry", "Check.Middle" })
        {
            var publisher = engine.GetNode(name);
            publisher.TargetState(engine.GetEvent("Check.Done")).Should().BeSameAs(engine.GetNode("End"));
            publisher.GetFSMTransitions().Count(t => t.Trigger == engine.GetEvent("Check.Done"))
                .Should().Be(1);
        }
        var resolvedOutput = engine.ModuleInterfaces.ResolveOutput("Check.Done");
        resolvedOutput.Event.Should().BeSameAs(engine.GetEvent("Check.Done"));
        resolvedOutput.Publishers.Select(n => n.Name).Should().BeEquivalentTo("Check.Entry", "Check.Middle");
        engine.ModuleInterfaces.ResolveInput("Check").Should().BeSameAs(engine.GetNode("Check.Entry"));
        var entry = engine.GetNode("Check.Entry");
        entry.UpdateEventDescriptions();
        entry.EventDescriptions.Select(d => (d.Index, d.Description)).Should().BeEquivalentTo(
            new[] { (1, "Check.Next"), (2, "Check.Done"), (3, "Check.Done") });
    }

    [Theory]
    [InlineData("Check.Unused -> End;", "Unused")]
    [InlineData("Check.Done -> End; Check.Done -> Other;", "Check.Done")]
    [InlineData("Check.Done -> End; Check.Done -> End;", "Check.Done")]
    [InlineData("Done -> Check to End;", "Check")]
    [InlineData("Done -> Check.Exit to End;", "Check.Exit")]
    [InlineData("Check.Done -> Start to End;", "Check.Done")]
    public void InvalidOutputConnections_FailClearly(string connection, string diagnostic)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action build = () => engine.CreateStateMachine(Checker.Replace("Done, Failed", "Done, Failed, Unused") + $$"""
            use Checker as Check;
            def Start(Step) {} def End(Step) {} def Other(Step) {}
            {{connection}}
            """);
        build.Should().Throw<ScriptException>().WithMessage($"*{diagnostic}*");
    }

    [Fact]
    public void OutputPublishers_DoNotConfuseDistinctCaseSensitiveInternalEvents()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine("""
            module Cases {
                input Entry; output Done;
                def Entry(Step) { 1->Done; }
                def Private(Step) { 1->done; }
            }
            use Cases as Check;
            def End(Step) {}
            Check.Done -> End;
            """);
        engine.GetEvent("Check.Done").Should().NotBeSameAs(engine.GetEvent("Check.done"));
        engine.GetNode("Check.Private").HasTransition(engine.GetEvent("Check.Done")).Should().BeFalse();
        engine.TryTransform("Check.done -> End;").Should().BeFalse("私有小写事件不是公开 Done 接口");
    }

    [Theory]
    [InlineData("input Missing;", "Missing")]
    [InlineData("input Entry, Entry;", "Entry")]
    [InlineData("output Done, Done;", "Done")]
    public void InvalidModuleInterfaces_ProduceScriptDiagnostics(string declarations, string diagnostic)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action build = () => engine.CreateStateMachine($$"""
            module Broken { {{declarations}} def Entry(Step) { 1->Done; } }
            use Broken as Check;
            """);
        build.Should().Throw<ScriptException>().WithMessage($"*{diagnostic}*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedLocalEvent_DoesNotShadowAQualifiedModuleTarget(bool localPrefixModule)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine(Checker + $$"""
            use Checker as Area.Check;
            namespace N {
                {{(localPrefixModule ? "use Checker as Area;" : "")}}
                def Start(Step) { 1->Launch; }
                def End(Step) {}
                def Area.Check event;
                Launch -> Start to Area.Check;
                Area.Check.Done -> End;
            }
            """);
        engine.GetNode("N.Start").TargetState(engine.GetEvent("N.Launch"))
            .Should().BeSameAs(engine.GetNode("Area.Check.Entry"));
        engine.GetNode("Area.Check.Exit").TargetState(engine.GetEvent("Area.Check.Done"))
            .Should().BeSameAs(engine.GetNode("N.End"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryDottedNames_SharingAnInstancePrefix_RemainOrdinary(bool beforeInstance)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        const string ordinary = "def Check.Other(Step) { 1->Check.Event; } def Check.Unused event;";
        engine.CreateStateMachine(Checker
            + (beforeInstance ? ordinary : "")
            + "use Checker as Check;"
            + (!beforeInstance ? ordinary : "")
            + "def End(Step) {} Check.Event -> Check.Other to End;");
        engine.GetNode("Check.Other").TargetState(engine.GetEvent("Check.Event"))
            .Should().BeSameAs(engine.GetNode("End"));
        engine.GetEvent("Check.Unused").Should().NotBeNull();
    }

    [Fact]
    public void DottedPublicInterfaces_AreNotShadowedByALongerUnrelatedInstanceName()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine("""
            module C {
                input Sub.Entry; output Sub.Done;
                def Sub.Entry(Step) { 1->Sub.Done; }
            }
            module D { input Other; def Other(Step) {} }
            use C as Check;
            use D as Check.Sub;
            def Start(Step) { 1->Launch; } def End(Step) {}
            Launch -> Start to Check.Sub.Entry;
            Check.Sub.Done -> End;
            """);
        engine.GetNode("Start").TargetState(engine.GetEvent("Launch"))
            .Should().BeSameAs(engine.GetNode("Check.Sub.Entry"));
        engine.GetNode("Check.Sub.Entry").TargetState(engine.GetEvent("Check.Sub.Done"))
            .Should().BeSameAs(engine.GetNode("End"));
        engine.ModuleInterfaces.ResolveInput("Check.Sub").Should().BeSameAs(engine.GetNode("Check.Sub.Other"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DistinctCaseSensitiveOrdinaryNames_DoNotCollideWithExpandedObjects(bool beforeInstance)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        const string ordinary = "def Check.entry(Step) { 1->Check.done; }";
        engine.CreateStateMachine(Checker
            + (beforeInstance ? ordinary : "") + "use Checker as Check;"
            + (!beforeInstance ? ordinary : "")
            + "def End(Step) {} Check.done -> Check.entry to End;");
        engine.GetEvent("Check.done").Should().NotBeSameAs(engine.GetEvent("Check.Done"));
        engine.GetNode("Check.entry").TargetState(engine.GetEvent("Check.done"))
            .Should().BeSameAs(engine.GetNode("End"));
        engine.GetNode("Check.Entry").HasTransition(engine.GetEvent("Check.done")).Should().BeFalse();
    }

    [Fact]
    public void NamespaceReferences_RespectRealInstanceIdentityAndOrdinaryDottedNames()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine(Checker + """
            namespace Area {
                use Checker as Check;
                use Checker as Other;
                def Start(Step) { success->Launch.Ready; }
                Launch.Ready -> Start to Area.Check;
                Check.Done -> Area.Other;
            }
            def Ordinary.Node(Step) { branch 9->Ordinary.Event; }
            def End(Step) {}
            Ordinary.Event -> Ordinary.Node to Area.Check.Entry;
            Area.Other.Done -> End;
            """);
        engine.GetNode("Area.Start").TargetState(engine.GetEvent("Area.Launch.Ready"))
            .Should().BeSameAs(engine.GetNode("Area.Check.Entry"));
        engine.GetNode("Ordinary.Node").TargetState(engine.GetEvent("Ordinary.Event"))
            .Should().BeSameAs(engine.GetNode("Area.Check.Entry"));
        engine.GetNode("Area.Check.Exit").TargetState(engine.GetEvent("Area.Check.Done"))
            .Should().BeSameAs(engine.GetNode("Area.Other.Entry"));
        engine.GetNode("Area.Other.Exit").TargetState(engine.GetEvent("Area.Other.Done"))
            .Should().BeSameAs(engine.GetNode("End"));
    }

    [Fact]
    public void Transform_ClearsOldTransitionsAndRebuildsPublicOutputConnectionsWithoutReplacingNodes()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine(Checker + """
            use Checker as Check;
            def Old(Step) {} def New(Step) {}
            Check.Done -> Old;
            """);
        var entry = engine.GetNode("Check.Entry");
        var exit = engine.GetNode("Check.Exit");
        var output = engine.GetEvent("Check.Done");
        engine.Transform("Check.Done -> New;");
        engine.GetNode("Check.Exit").Should().BeSameAs(exit);
        exit.TargetState(output).Should().BeSameAs(engine.GetNode("New"));
        exit.UpdateEventDescriptions();
        exit.EventDescriptions.Single().Description.Should().Be("Check.Done");
        // Transform 一贯清除所有旧连线（包括实例内连线），不重新定义节点或隐式恢复它们。
        entry.HasTransition(engine.GetEvent("Check.Next")).Should().BeFalse();
        engine.TryTransform("Check.Missing -> New;").Should().BeFalse();
        engine.TryTransform("Check.Done -> New").Should().BeFalse();
    }

    [Fact]
    public void InterfaceConnection_ConflictingWithInternalTransition_FailsWithoutRebindingBranch()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action build = () => engine.CreateStateMachine("""
            module Flow {
                input Entry; output Done;
                def Entry(Step) { success->Done; }
                def Exit(Step) {}
                Done -> Entry to Exit;
            }
            use Flow as Check;
            def End(Step) {}
            Check.Done -> End;
            """);
        build.Should().Throw<ScriptException>().WithMessage("*Check.Entry*Check.Done*冲突*");
        var node = engine.GetNode("Check.Entry");
        node.UpdateEventDescriptions();
        node.EventDescriptions.Single().Description.Should().Be("Check.Done");
        node.TargetState(engine.GetEvent("Check.Done")).Should().BeSameAs(engine.GetNode("Check.Exit"));
    }

    [Fact]
    public void ExplicitInstances_ConnectOrdinaryNodesAndModulesWithoutRuntimeProxies()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine(Checker + """
            use Checker as Left;
            use Checker as Right { Pos:(12.5,-3); Color:"red"; FlowID:check_b; }
            def Start(Step) { success->Launch; }
            def End(Step) { }
            Launch -> Start to Left;
            Left.Done -> Right;
            Right.Done -> End;
            """);

        engine.GetNodeNames().Should().BeEquivalentTo(
            "Start", "End", "Left.Entry", "Left.Exit", "Right.Entry", "Right.Exit");
        engine.GetNode("Start").TargetState(engine.GetEvent("Launch"))
            .Should().BeSameAs(engine.GetNode("Left.Entry"));
        engine.GetNode("Left.Exit").TargetState(engine.GetEvent("Left.Done"))
            .Should().BeSameAs(engine.GetNode("Right.Entry"));
        engine.GetNode("Right.Exit").TargetState(engine.GetEvent("Right.Done"))
            .Should().BeSameAs(engine.GetNode("End"));
        var instance = engine.ModuleInstances["Right"];
        (instance.PosX, instance.PosY, instance.Color, instance.FlowID)
            .Should().Be((12.5, -3, "red", "check_b"));
    }
}
