using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace StateMachineTest;

// 从原有未跟踪夹具迁移：保留真实发布者、隔离、入口和非法映射不变量；原文留在 .scratch/t1-module-dsl/originals。
public class ModuleScriptFacts
{
    private const string CheckerModule = """
        module Checker {
            input Entry;
            output Done, Failed;
            def Entry(Step) { 1->Next; 3->Failed; }
            def Exit(Step) { 1->Done; }
            Next->Entry to Exit;
        }
        """;

    [Theory]
    [InlineData("Check.Next")]
    [InlineData("check.next")]
    [InlineData("Accepted")]
    public void OutputMapping_IsRejectedInsteadOfRebindingBranches(string externalEvent)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule);
        Action create = () => engine.CreateStateMachine($$"""
            use Checker as Check { Done->{{externalEvent}}; }
            """);
        create.Should().Throw<ScriptException>().WithMessage("*Check*映射*");
    }

    [Theory]
    [InlineData("", -1)]
    [InlineData("", 0)]
    [InlineData("", 2)]
    [InlineData("branch ", -1)]
    [InlineData("branch ", 0)]
    [InlineData("branch ", 2)]
    public void OutputMapping_RejectsAllNumericIndices(string branchKeyword, int index)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule);
        Action create = () => engine.CreateStateMachine($$"""
            use Checker as Check { {{branchKeyword}}{{index}}->Accepted; }
            """);
        create.Should().Throw<ScriptException>().WithMessage("*Check*映射*");
    }

    [Theory]
    [InlineData("Check.Missing", "Missing")]
    [InlineData("Check.Next", "Next")]
    [InlineData("Missing.Done", "Missing")]
    [InlineData("Target.Done", "Target")]
    public void OutputConnection_RejectsUnknownOrPrivateInterfaces(string output, string diagnostic)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule);
        Action create = () => engine.CreateStateMachine($$"""
            use Checker as Check;
            def Target(Step) { }
            {{output}} -> Target;
            """);
        create.Should().Throw<ScriptException>().WithMessage($"*{diagnostic}*");
    }

    [Fact]
    public void OutputConnection_UsesActualPublishersIncludingNonTerminalInputNode()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule);
        engine.CreateStateMachine("""
            use Checker as Check;
            def Success(Step) { }
            def Failure(Step) { }
            Check.Done -> Success;
            Check.Failed -> Failure;
            """);
        var entry = engine.GetNode("Check.Entry");
        var exit = engine.GetNode("Check.Exit");
        entry.TargetState(engine.GetEvent("Check.Failed")).Should().BeSameAs(engine.GetNode("Failure"));
        exit.TargetState(engine.GetEvent("Check.Done")).Should().BeSameAs(engine.GetNode("Success"));
        exit.HasTransition(engine.GetEvent("Check.Failed")).Should().BeFalse();
        exit.UpdateEventDescriptions();
        exit.EventDescriptions.Select(d => (d.Index, d.Description)).Should()
            .BeEquivalentTo(new[] { (1, "Check.Done") });
    }

    [Fact]
    public void TransitionToModuleInstance_ConnectsToDeclaredInput()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule);
        engine.CreateStateMachine("""
            def Start(Step) { 1->Begin; }
            use Checker as Check;
            Begin->Start to Check;
            """);
        engine.GetNode("Start").TargetState(engine.GetEvent("Begin"))
            .Should().BeSameAs(engine.GetNode("Check.Entry"));
    }

    [Theory]
    [InlineData("Done, Failed")]
    [InlineData("Failed, Done")]
    public void OutputOrder_DoesNotChangeBranchOrConnectionIdentity(string outputs)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule.Replace("Done, Failed", outputs));
        engine.CreateStateMachine("""
            use Checker as Left;
            use Checker as Right;
            def Target(Step) { }
            def Done as event;
            Left.Done -> Target;
            """);
        engine.GetNode("Left.Exit").TargetState(engine.GetEvent("Left.Done"))
            .Should().BeSameAs(engine.GetNode("Target"));
        engine.GetNode("Right.Exit").HasTransition(engine.GetEvent("Left.Done")).Should().BeFalse();
        engine.GetEventNames().Should().BeEquivalentTo(
            "Left.Next", "Left.Done", "Left.Failed", "Right.Next", "Right.Done", "Right.Failed", "Done");
        engine.GetEvent("Done").Should().NotBeSameAs(engine.GetEvent("Left.Done"));
        engine.GetEvent("Right.Done").Should().NotBeSameAs(engine.GetEvent("Left.Done"));
    }

    [Fact]
    public void ModuleInstances_KeepInternalTransitionsWithinEachInstance()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule);
        engine.CreateStateMachine("use Checker as Left; use Checker as Right;");
        engine.GetNode("Left.Entry").TargetState(engine.GetEvent("Left.Next"))
            .Should().BeSameAs(engine.GetNode("Left.Exit"));
        engine.GetNode("Right.Entry").TargetState(engine.GetEvent("Right.Next"))
            .Should().BeSameAs(engine.GetNode("Right.Exit"));
        engine.GetNode("Left.Entry").HasTransition(engine.GetEvent("Right.Next")).Should().BeFalse();
        engine.GetNode("Right.Entry").HasTransition(engine.GetEvent("Left.Next")).Should().BeFalse();
    }

    [Fact]
    public void ModuleInstances_KeepNodesAndEventsInSeparateNamespaces()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", CheckerModule);
        engine.CreateStateMachine("use Checker as Left; use Checker as Right;");
        engine.GetNodeNames().Should().BeEquivalentTo("Left.Entry", "Left.Exit", "Right.Entry", "Right.Exit");
        engine.GetNode("Left.Entry").Should().NotBeSameAs(engine.GetNode("Right.Entry"));
        engine.GetEventNames().Should().BeEquivalentTo(
            "Left.Next", "Left.Done", "Left.Failed", "Right.Next", "Right.Done", "Right.Failed");
        engine.GetEvent("Left.Next").Should().NotBeSameAs(engine.GetEvent("Right.Next"));
    }
}
