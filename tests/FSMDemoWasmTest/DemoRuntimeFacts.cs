using FSMDemo.Wasm.Services;

namespace FSMDemoWasmTest;

public class DemoRuntimeFacts
{
    private const string SamePrefixScript = """
        module M {
            input Entry, Retry;
            output Done;
            def Entry(Idle) { 1->Next; }
            def Retry(Idle) { 1->Done; }
            Next -> Entry to Retry;
        }
        use M as A;
        def Start(Start) { 1->Launch; }
        def A.Observer(Idle) { 1->Observe; }
        def End(End) {}
        Launch -> Start to A.Retry;
        Observe -> A.Observer to End;
        A.Done -> End;
        """;

    [Fact]
    public void DefaultDemo_CollapsesBothInstancesAndShowsTheirInternalTransitions()
    {
        using var runtime = new DemoRuntime();
        runtime.Error.Should().BeNull();
        runtime.Nodes.Select(n => n.Name).Should().BeEquivalentTo("Start", "CheckA", "CheckB", "End", "ErrorEnd");
        runtime.Connections.Should().BeEquivalentTo(new[]
        {
            new FsmConnectionView("Start", "CheckA", "StartEvent"),
            new FsmConnectionView("CheckA", "CheckB", "CheckA.OK"),
            new FsmConnectionView("CheckA", "ErrorEnd", "CheckA.NG"),
            new FsmConnectionView("CheckB", "End", "CheckB.OK"),
            new FsmConnectionView("CheckB", "ErrorEnd", "CheckB.NG")
        });

        foreach (var instance in new[] { "CheckA", "CheckB" })
        {
            runtime.EnterModule(instance).Should().BeTrue();
            runtime.Nodes.Select(n => n.Name).Should().BeEquivalentTo($"{instance}.Entry", $"{instance}.Exit");
            runtime.Connections.Should().Equal(new FsmConnectionView($"{instance}.Entry", $"{instance}.Exit", $"{instance}.NextEvent"));
            runtime.ExitModule();
        }
    }

    [Fact]
    public void ImportOrdinaryScript_ReplacesModuleViewWithoutCollapsingDottedNames()
    {
        using var runtime = new DemoRuntime();
        runtime.EnterModule("CheckA").Should().BeTrue();
        runtime.ImportScript("""
            def CheckA.Observer(Idle) { 1->Observe; }
            def End(End) {}
            Observe -> CheckA.Observer to End;
            """);

        runtime.Error.Should().BeNull();
        runtime.ViewModule.Should().BeNull();
        runtime.Nodes.Select(n => n.Name).Should().BeEquivalentTo("CheckA.Observer", "End");
        runtime.Nodes.Should().OnlyContain(n => !n.IsModuleInstance);
        runtime.Connections.Should().Equal(new FsmConnectionView("CheckA.Observer", "End", "Observe"));
        runtime.EnterModule("CheckA").Should().BeFalse();
    }

    [Fact]
    public async Task Execution_RealModuleEndNodeDoesNotStopBeforeItsOutputTarget()
    {
        using var runtime = new DemoRuntime();
        runtime.ImportScript("""
            module M { input Entry; output Done; def Entry(End) { 1->Done; } }
            use M as A;
            def End(End) { 1->EndEvent; }
            A.Done -> End;
            """);
        runtime.Error.Should().BeNull();

        await RunToCompletion(runtime, "A.Entry");

        runtime.Logs.Should().Contain("Enter End");
    }

    [Fact]
    public async Task Execution_OrdinarySamePrefixEndNodeFinishesWithoutFollowingItsTransition()
    {
        using var runtime = new DemoRuntime();
        runtime.ImportScript("""
            module M { input Entry; output Done; def Entry(End) { 1->Done; } }
            use M as A;
            def A.Finish(End) { 1->Complete; }
            def UnexpectedEnd(End) { 1->EndEvent; }
            Complete -> A.Finish to UnexpectedEnd;
            """);
        runtime.Error.Should().BeNull();

        await RunToCompletion(runtime, "A.Finish");

        runtime.Logs.Should().NotContain("Enter UnexpectedEnd");
    }

    private static async Task RunToCompletion(DemoRuntime runtime, string startNode)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (runtime.State == "Finished") finished.TrySetResult();
        }
        runtime.Changed += OnChanged;
        try
        {
            await runtime.StartAsync(startNode);
            runtime.Error.Should().BeNull();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await runtime.StopAsync();
            runtime.Changed -= OnChanged;
        }
    }

    [Fact]
    public void Connections_CollapseOnlyRealMembersAndExcludeOrdinaryNodesFromModuleView()
    {
        using var runtime = new DemoRuntime();
        runtime.ImportScript(SamePrefixScript + """

            def A.Source(Idle) { 1->Send; }
            Send -> A.Source to A.Observer;
            """);

        runtime.Error.Should().BeNull();
        runtime.Connections.Should().BeEquivalentTo(new[]
        {
            new FsmConnectionView("Start", "A", "Launch"),
            new FsmConnectionView("A", "End", "A.Done"),
            new FsmConnectionView("A.Observer", "End", "Observe"),
            new FsmConnectionView("A.Source", "A.Observer", "Send")
        });

        runtime.EnterModule("A").Should().BeTrue();
        runtime.Connections.Should().Equal(new FsmConnectionView("A.Entry", "A.Retry", "A.Next"));
    }

    [Fact]
    public void RootAndModuleViews_KeepOrdinarySamePrefixNodesOutsideTheModule()
    {
        using var runtime = new DemoRuntime();
        runtime.ImportScript(SamePrefixScript);

        runtime.Error.Should().BeNull();
        runtime.Nodes.Select(n => n.Name).Should().BeEquivalentTo("Start", "A", "A.Observer", "End");
        runtime.Nodes.Single(n => n.Name == "A.Observer").IsModuleInstance.Should().BeFalse();
        runtime.Nodes.Single(n => n.Name == "A").IsModuleInstance.Should().BeTrue();

        runtime.EnterModule("A").Should().BeTrue();
        runtime.Nodes.Select(n => n.Name).Should().BeEquivalentTo("A.Entry", "A.Retry");
        runtime.Nodes.Select(n => n.DisplayName).Should().BeEquivalentTo("Entry", "Retry");

        runtime.ExitModule();
        runtime.Nodes.Select(n => n.Name).Should().BeEquivalentTo("Start", "A", "A.Observer", "End");
    }
}
