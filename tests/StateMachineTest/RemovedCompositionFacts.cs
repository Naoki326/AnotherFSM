using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace StateMachineTest;

public class RemovedCompositionFacts
{
    private sealed class ObservableNode : BaseFSMNode, IObservable<StateTrackInfo>
    {
        private readonly System.Reactive.Subjects.Subject<StateTrackInfo> _observations = new();
        public IDisposable Subscribe(IObserver<StateTrackInfo> observer) => _observations.Subscribe(observer);
        protected override Task ExecuteAsync()
        {
            _observations.OnNext(new StateTrackInfo { IsEnter = true, StateName = "Legacy.Child" });
            PublishEvent(FSMEnum.Next);
            return Task.CompletedTask;
        }
    }

    private sealed class ObservableFactory : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new ObservableNode();
        public Type GetNodeType(string key) => typeof(ObservableNode);
        public string GetNodeFeatureName(Type type) => "Ordinary";
    }

    [Fact]
    public async Task NodeObservationsAreNotForwardedAsNestedExecutorNotifications()
    {
        var engine = new FSMEngine(new ObservableFactory());
        engine.CreateStateMachine("def Actual(Ordinary) { 1->Done; }");
        var activated = new List<string>();
        using var executor = await engine.Prepare().StartAsync("Actual", new[] { "Done" },
            value => value.NodeStateChanged += (_, name) => activated.Add(name), true);
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        activated.Should().Equal("Actual");
    }

    [Theory]
    [InlineData("def Old(Group) {}")]
    [InlineData("def Old(Parallel) {}")]
    [InlineData("def Old(GroupT) {}")]
    [InlineData("def Old(GroupTU) {}")]
    [InlineData("def Old(ParallelT) {}")]
    [InlineData("def Old(ParallelTU) {}")]
    [InlineData("def Old(group) {}")]
    [InlineData("def Old(Step) { [Entry->Done]; }")]
    [InlineData("module Legacy { input Old; def Old(Group) {} }")]
    [InlineData("module Legacy { input Old; def Old(Step) { [Entry->Done]; } }")]
    public void LegacyCompositionFailsDuringAssemblyWithMigrationDiagnostic(string script)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action build = () => engine.CreateStateMachine(script);
        build.Should().Throw<ScriptException>().WithMessage("*Module*非等价*");
        engine.TryCreateStateMachine(script).Should().BeFalse();
    }

    [Theory]
    [InlineData("def Old(Group) {}")]
    [InlineData("def Old(Step) { [Entry->Done]; }")]
    public void TransformCannotSilentlyIgnoreRemovedComposition(string script)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        engine.CreateStateMachine("def Start(Step) { 1->Go; } def End(Step) {} Go -> Start to End;");
        Action transform = () => engine.Transform(script);
        transform.Should().Throw<ScriptException>().WithMessage("*Module*not equivalent*");
        engine.GetNode("Start").TargetState(engine.GetEvent("Go")).Should().BeSameAs(engine.GetNode("End"));
        engine.TryTransform(script).Should().BeFalse();
    }

    [Fact]
    public void RemovedTypeDiagnosticIncludesAvailableSourcePosition()
    {
        Action build = () => new FSMEngine(new ScriptNodeFactory()).CreateStateMachine("\n  def Old(Group) {}");
        build.Should().Throw<ScriptException>().WithMessage("*line 2, column 3*");
    }

    [Theory]
    [InlineData("GroupT")]
    [InlineData("ParallelTU")]
    public void DirectAndFluentAssemblyRejectRemovedTypeKeys(string type)
    {
        var engine = new FSMEngine(new ScriptNodeFactory());
        Action create = () => engine.CreateNode(type, "Old");
        create.Should().Throw<ScriptException>().WithMessage("*Module*not equivalent*");
        engine.TryCreateNode(type, "Old").Should().BeFalse();
        Action fluent = () => FSMEngineBuilder.Create(new ScriptNodeFactory())
            .ConfigureFSMDefine(builder => builder.AddNode("Old", type)).Build();
        fluent.Should().Throw<ScriptException>().WithMessage("*Module*not equivalent*");
    }
}
