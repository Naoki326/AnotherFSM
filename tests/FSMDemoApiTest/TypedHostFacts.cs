using FSMDemo.Runtime;
using FSMDemo.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StateMachine;

namespace FSMDemoApiTest;

public class TypedHostFacts
{
    public const string Script = """
        module Work {
            input Query; output Done;
            def Query(BusinessQuery) {1->Next;}
            def Process(BusinessProcess) {1->Done;}
            Next -> Query to Process;
        }
        use Work as A;
        def End(End) {1->EndEvent;}
        A.Done -> End;
        """;

    [Fact]
    public async Task ExternallyPreparedEngineRejectsStartBeforeChangingGraphOrHostInput()
    {
        await using var host = new EngineManager(NullLogger<EngineManager>.Instance, new BrowserNodeFactory(), true, DemoBusinessHost.Create());
        await host.ImportAsync("def Query(BusinessQuery) {1->Done;}");
        using var external = host.Engine.BindContext<DemoNodes.DemoBusinessInput>().Prepare(new DemoNodes.DemoBusinessInput());
        var before = host.Engine.GetEventNames().ToArray();
        Func<Task> conflict = () => host.StartExecution("Query", "NewEnd");
        await conflict.Should().ThrowAsync<FSMException>();
        host.Engine.GetEventNames().Should().Equal(before);
        host.GetExecutionStatus().BusinessRun.Should().BeNull();
        host.CurrentState.Should().Be(ExecutionState.Idle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealBrowserFactoryWrongKeyPreservesNodeIdentityAndRootCause(bool dsl)
    {
        var engine = new FSMEngine(new BrowserNodeFactory());
        Action assemble = () => { if (dsl) engine.CreateStateMachine("def Area.Broken(NoSuchKey) {}"); else engine.CreateNode("NoSuchKey", "Area.Broken"); };
        var error = assemble.Should().Throw<Exception>().Which;
        error.Message.Should().Contain("Area.Broken").And.Contain("NoSuchKey").And.Contain("not available");
        error.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task MissingNecessarySourceFailsBeforeNodesAndCandidateAssemblyPreservesOldExecution()
    {
        await using var host = new EngineManager(NullLogger<EngineManager>.Instance, new BrowserNodeFactory(), true,
            DemoBusinessHost.Create());
        await host.ImportAsync("def Approval(BusinessApproval) {1->Done;}");
        var activated = new List<string>();
        host.NodeActivated += activated.Add;
        Func<Task> start = () => host.StartExecution("Approval", "Done");
        (await start.Should().ThrowAsync<FSMException>()).Which.Message.Should().Contain("Approval").And.Contain("IApprovalBusiness").And.Contain("null");
        activated.Should().BeEmpty();
        host.CurrentState.Should().Be(ExecutionState.Idle);
        await host.ImportAsync("def Wait(Idle) {1->Done;}");
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.StateChanged += (state, _) => { if (state == ExecutionState.Running) running.TrySetResult(); };
        await host.StartExecution("Wait", "Done");
        await running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old = host.Executor;
        Func<Task> invalid = () => host.ImportAsync("def Broken(NoSuchKey) {}");
        await invalid.Should().ThrowAsync<Exception>();
        host.Executor.Should().BeSameAs(old);
        host.CurrentState.Should().Be(ExecutionState.Running);
    }

    [Fact]
    public async Task ScanningHostExplicitlyPairsAggregateAndCreatesFreshInputPerExecution()
    {
        await using var host = new EngineManager(NullLogger<EngineManager>.Instance, new AssemblyScanningNodeFactory(),
            context: DemoBusinessHost.Create());
        await host.ImportAsync(Script);
        for (var run = 1; run <= 2; run++)
        {
            (await host.StartExecution("A.Query", "EndEvent")).Should().BeTrue();
            await host.Executor!.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            host.GetExecutionStatus().BusinessResult.Should().Be(42);
            host.GetExecutionStatus().BusinessRun.Should().Be(run);
            await host.StopExecution();
        }
    }
}
