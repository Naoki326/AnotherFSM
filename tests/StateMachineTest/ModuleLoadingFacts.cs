using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace StateMachineTest;

public class ModuleLoadingFacts
{
    private const string Module = """
        module Checker {
            input Entry; output Done;
            def Entry(Step) { 1->Done; }
        }
        """;

    [Fact]
    public void RelativeImports_AllowRepeatedAndDiamondDependencies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anotherfsm-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Checker.fsm"), Module);
            File.WriteAllText(Path.Combine(directory, "Left.fsm"), "import Checker; module Left { input Entry; def Entry(Step) {} }");
            File.WriteAllText(Path.Combine(directory, "Right.fsm"), "import Checker; module Right { input Entry; def Entry(Step) {} }");
            var main = Path.Combine(directory, "main.fsm");
            File.WriteAllText(main, """
                import Left;
                import Right;
                import Checker as Alias;
                use Alias as Check;
                def End(Step) {}
                Check.Done -> End;
                """);
            var engine = new FSMEngine(new ScriptNodeFactory());
            engine.CreateStateMachineByFile(main);
            engine.GetNode("Check.Entry").TargetState(engine.GetEvent("Check.Done"))
                .Should().BeSameAs(engine.GetNode("End"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RegisteredModules_LoadTheirRelativeDependencies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anotherfsm-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Dependency.fsm"), "module Dependency { input Entry; def Entry(Step) {} }");
            var engine = new FSMEngine(new ScriptNodeFactory());
            engine.RegisterModule("Checker", "import Dependency; " + Module, directory);
            engine.CreateStateMachine("use Checker as Check;");
            engine.GetNode("Check.Entry").Should().NotBeNull();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("import Missing;", "Missing")]
    [InlineData("use Missing as Check;", "Missing")]
    [InlineData("def Old(Checker) {}", "use Checker as")]
    [InlineData("def Old as Checker state {}", "use Checker as")]
    public void MissingOrObsoleteDeclarations_HaveActionableFailures(string script, string message)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("Checker", Module);
        Action build = () => engine.CreateStateMachine(script);
        build.Should().Throw<ScriptException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void CircularImports_FailWithoutRecursingIndefinitely()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("A", "import B; module A { def Entry(Step) {} }");
        engine.RegisterModule("B", "import A; module B { def Entry(Step) {} }");
        Action build = () => engine.CreateStateMachine("import A; use A as Check;");
        build.Should().Throw<ScriptException>().WithMessage("*Circular*A*");
    }

    [Fact]
    public void RebuildingTheSameInlineModuleScript_RefreshesAliasesWithoutFalseCollisions()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        var script = Module + "import Checker as Alias; use Alias as Check;";
        engine.CreateStateMachine(script);
        var firstNode = engine.GetNode("Check.Entry");
        engine.CreateStateMachine(script);
        engine.GetNodeNames().Should().BeEquivalentTo("Check.Entry");
        engine.GetNode("Check.Entry").Should().NotBeSameAs(firstNode);
        engine.GetEventNames().Should().BeEquivalentTo("Check.Done");
    }

    [Theory]
    [InlineData("module First { input Original; def Original(Step) {} } import First as Alias; module Second as Alias { def Replacement(Step) {} }")]
    [InlineData("module First { def Original(Step) {} } module First { def Replacement(Step) {} }")]
    public void ModuleDeclarations_CannotOverwriteDefinitionsOrImportAliases(string script)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action build = () => engine.CreateStateMachine(script);
        build.Should().Throw<ScriptException>().WithMessage("*碰撞*");
    }

    [Fact]
    public void ImportAlias_CannotOverwriteAnExistingModuleDefinition()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.RegisterModule("First", "module First { input Original; def Original(Step) {} }");
        engine.RegisterModule("Second", "module Second { input Replacement; def Replacement(Step) {} }");
        Action build = () => engine.CreateStateMachine("import First; import Second as First; use First as Check;");
        build.Should().Throw<ScriptException>().WithMessage("*First*碰撞*");
        engine.CreateStateMachine("use First as Check;");
        engine.GetNodeNames().Should().BeEquivalentTo("Check.Original");
    }

    [Fact]
    public void GenuineNodeType_SharingAModuleName_RemainsAnOrdinaryNode()
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine("""
            module Step { input Entry; def Entry(Step) {} }
            def Ordinary(Step) { branch 7->Seven; success->Done; }
            def End(Step) {}
            Seven -> Ordinary to End;
            """);
        engine.ModuleInstances.Should().BeEmpty();
        engine.GetNodeNames().Should().BeEquivalentTo("Ordinary", "End");
        engine.GetNode("Ordinary").TargetState(engine.GetEvent("Seven"))
            .Should().BeSameAs(engine.GetNode("End"));
    }
}
