using Autofac;
using DemoNodes;
using FSMDemo.API.Services;
using FSMDemo.Runtime;
using StateMachine;

namespace FSMDemoApiTest;

public class TypedConsumerFacts
{
    private const string Module = """
        input Query; output Done;
        def Query(BusinessQuery) {1->Next;}
        def Process(BusinessProcess) {1->Done; 2->Done;}
        Next -> Query to Process;
        """;
    private const string Script = """
        module Work {
            input Query; output Done;
            def Query(BusinessQuery) {1->Next;}
            def Process(BusinessProcess) {1->Done; 2->Done;}
            Next -> Query to Process;
        }
        namespace Area { use Work as A; use Work as B; }
        Area.A.Done -> Area.B;
        """;
    public sealed class Pair
    {
        public DemoJob Job { get; init; } = new();
        public DemoJob Other { get; init; } = new();
        public static void Configure(FSMContextBindings<Pair> bindings)
        {
            bindings.DefaultSource(x => x.Job);
            bindings.Node<IProcessBusiness>("Area.B.Process", x => x.Other);
        }
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("fluent")]
    [InlineData("fluent-module")]
    [InlineData("dsl")]
    public async Task EveryRealAssemblyPathUsesTheSameTypedPlan(string path)
    {
        var factory = new BrowserNodeFactory();
        var engine = new FSMEngine(factory);
        var start = "Query";
        var end = "Done";
        if (path == "direct")
        {
            engine.CreateNode<BusinessQueryNode>("Query");
            engine.CreateNode<BusinessProcessNode>("Process");
            engine.AttachEvent("Query", "Next", 1);
            engine.AttachEvent("Process", "Done", 1);
            engine.ConnectNode("Next", "Query", "Process");
        }
        else if (path == "fluent")
            engine = FSMEngineBuilder.Create(factory).ConfigureFSMDefine(b => b
                .AddNode<BusinessQueryNode>("Query", n => n.SetEventBinding(1, "Next"))
                .AddNode<BusinessProcessNode>("Process", n => n.SetEventBinding(1, "Done"))
                .AddConnection("Next", "Query", "Process")).Build();
        else if (path == "fluent-module")
        {
            engine = FSMEngineBuilder.Create(factory).ConfigureModule("Work", "module Work {" + Module + "}")
                .ConfigureFSMDefine(b => b.AddModule("Work", "A")).Build();
            start = "A.Query"; end = "A.Done";
        }
        else
        {
            engine.CreateStateMachine("module Work {" + Module + "} use Work as A;");
            start = "A.Query"; end = "A.Done";
        }
        var input = new DemoBusinessInput();
        using var prepared = engine.BindContext<DemoBusinessInput>().Prepare(input);
        using var executor = await prepared.StartAsync(start, end);
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        input.Job.Value.Should().Be(42);
    }

    [Fact]
    public async Task RealModuleOverridesKeepNamespaceIdentityBranchPublishersAndIndependentBusinessObjects()
    {
        var engine = new FSMEngine(new BrowserNodeFactory());
        engine.CreateStateMachine(Script);
        var input = new Pair { Other = new DemoJob { Value = 100 } };
        var plan = engine.BindContext<Pair>(Pair.Configure);
        using var executor = await plan.Prepare(input).StartAsync("Area.A.Query", "Area.B.Done");
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        input.Job.Value.Should().Be(41); // B.Query writes the shared Job again.
        input.Other.Value.Should().Be(101); // B.Process is not rebound by its predecessor.
        engine.ModuleInstances["Area.B"].ExpandedNodeNames.Should().BeEquivalentTo("Area.B.Query", "Area.B.Process");
        engine.ModuleInterfaces.ResolveOutput("Area.A.Done").Publishers.Select(n => n.Name).Should().Equal("Area.A.Process");
        engine.GetNode("Area.A.Process").UpdateEventDescriptions();
        engine.GetNode("Area.A.Process").EventDescriptions.Select(b => b.Description).Should().Equal("Area.A.Done", "Area.A.Done");
        engine.GetEventNames().Should().NotContain("Done");
    }

    [Fact]
    public void RealModuleLocalEntryStillRequiresTheFullPlanAndReportsItsQualifiedSource()
    {
        var engine = new FSMEngine(new BrowserNodeFactory());
        engine.CreateStateMachine(Script);
        Action ambiguous = () => engine.BindContext<Pair>();
        ambiguous.Should().Throw<FSMException>().WithMessage("*Area.A.Query*IQueryBusiness*Job*Other*");
        var plan = engine.BindContext<Pair>("Area.A.Query", "Area.A.Done", Pair.Configure);
        Action missing = () => plan.Prepare(new Pair { Other = null! });
        missing.Should().Throw<FSMException>().WithMessage("*Area.B.Process*IProcessBusiness*Other*null*");
        Action unknown = () => engine.BindContext<Pair>(b => b.Node<IQueryBusiness>("Area.B.Missing", x => x.Job));
        unknown.Should().Throw<FSMException>().WithMessage("*Area.B.Missing*IQueryBusiness*Job*");
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("browser")]
    [InlineData("autofac")]
    public async Task ActualProductionFactoriesDeclareCreateBindAndExecuteBusinessNodes(string path)
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<BusinessQueryNode>().Keyed<IFSMNode>("BusinessQuery");
        builder.RegisterType<BusinessProcessNode>().Keyed<IFSMNode>("BusinessProcess");
        using var container = builder.Build();
        IFSMNodeFactory factory = path switch
        {
            "scan" => new AssemblyScanningNodeFactory(), "browser" => new BrowserNodeFactory(),
            _ => new AutofacNodeFactory(container)
        };
        var engine = new FSMEngine(factory);
        engine.CreateStateMachine("module Work {" + Module + "} use Work as Actual;");
        var input = new DemoBusinessInput();
        using var executor = await engine.BindContext<DemoBusinessInput>().Prepare(input).StartAsync("Actual.Query", "Actual.Done");
        await executor.Execution.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        input.Job.Value.Should().Be(42);
        factory.GetNodeType("BusinessQuery").Should().Be(typeof(BusinessQueryNode));
        engine.GetNode("Actual.Process").Should().BeOfType<BusinessProcessNode>();
        Action badKey = () => engine.CreateNode("WrongKey", "Actual.Bad");
        badKey.Should().Throw<Exception>().WithMessage("*Actual.Bad*WrongKey*");
    }

    [Fact]
    public void RealAutofacDelegateMetadataCannotHideActualBusinessRequirement()
    {
        var builder = new ContainerBuilder();
        builder.Register<IFSMNode>(_ => new BusinessQueryNode()).Keyed<IFSMNode>("BadMetadata");
        using var container = builder.Build();
        var engine = new FSMEngine(new AutofacNodeFactory(container));
        engine.CreateStateMachine("def Area.Bad(BadMetadata) {1->Done;}");
        Action bind = () => engine.BindContext<DemoBusinessInput>();
        bind.Should().Throw<FSMException>().WithMessage("*Area.Bad*BadMetadata*IFSMNode*BusinessQueryNode*IQueryBusiness*");
    }
}
