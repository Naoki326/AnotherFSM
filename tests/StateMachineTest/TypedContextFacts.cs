using System.Linq.Expressions;
using StateMachine;

namespace StateMachineTest;

public class TypedContextFacts
{
    public interface IQuery { string? Tasks { get; set; } }
    public interface IProcess { string? Tasks { get; } }
    public sealed class Job : IQuery, IProcess { public string? Tasks { get; set; } }
    public sealed class Input { public Job Job { get; init; } = new(); }

    private sealed class Query : BaseFSMNode<IQuery>
    {
        protected override Task ExecuteAsync()
        {
            Context.Tasks = "queried";
            PublishEvent(1);
            return Task.CompletedTask;
        }
    }
    private sealed class Process(List<string?> results) : BaseFSMNode<IProcess>
    {
        protected override Task ExecuteAsync()
        {
            results.Add(Context.Tasks);
            PublishEvent(1);
            return Task.CompletedTask;
        }
    }
    private sealed class Factory(List<string?> results) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => key == "Query" ? new Query() : new Process(results);
        public Type GetNodeType(string key) => key == "Query" ? typeof(Query) : typeof(Process);
        public string GetNodeFeatureName(Type type) => type == typeof(Query) ? "Query" : "Process";
    }
    private static FSMEngine Engine(List<string?> results)
    {
        var engine = new FSMEngine(new Factory(results));
        engine.CreateNode("Query", "Query");
        engine.CreateNode("Process", "Process");
        engine.AttachEvent("Query", "Next", 1);
        engine.AttachEvent("Process", "Done", 1);
        engine.ConnectNode("Next", "Query", "Process");
        return engine;
    }

    public sealed class MultipleInput
    {
        public Job Job { get; init; } = new();
        public Job Other { get; init; } = new() { Tasks = "exception" };
        public static void Configure(FSMContextBindings<MultipleInput> bindings)
        {
            bindings.DefaultSource(x => x.Job);
            bindings.Node<IProcess>("Work.Process", x => x.Other);
        }
    }

    [Fact]
    public async Task QualifiedNodeExceptionWinsOverTheSharedDefaultAndSurvivesTransition()
    {
        var results = new List<string?>();
        var engine = Engine(results);
        engine.ChangeNodeName("Process", "Work.Process");
        var flow = engine.BindContext<MultipleInput>(MultipleInput.Configure);
        var input = new MultipleInput();
        using var executor = await flow.Prepare(input).StartAsync("Query", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("exception");
        input.Job.Tasks.Should().Be("queried");
        input.Other.Tasks.Should().Be("exception");
    }

    [Theory]
    [InlineData("duplicate-default")]
    [InlineData("duplicate-node")]
    [InlineData("unknown-node")]
    [InlineData("wrong-requirement")]
    [InlineData("defaults-hidden-by-overrides")]
    [InlineData("null-node")]
    [InlineData("null-expression")]
    public void InvalidRelationshipsAreRejectedBeforeAnExecutablePlanExists(string scenario)
    {
        var results = new List<string?>();
        Action assemble = () => Engine(results).BindContext<MultipleInput>(b =>
        {
            b.DefaultSource(x => x.Job);
            switch (scenario)
            {
                case "duplicate-default": b.Default<IQuery>(x => x.Other); break;
                case "duplicate-node":
                    b.Node<IQuery>("Query", x => x.Job);
                    b.Node<IQuery>("Query", x => x.Job); break;
                case "unknown-node": b.Node<IQuery>("Work.Missing", x => x.Other); break;
                case "wrong-requirement": b.Node<IProcess>("Query", x => x.Other); break;
                case "null-node": b.Node<IQuery>(null!, x => x.Job); break;
                case "null-expression": b.Node<IQuery>("Query", null!); break;
                case "defaults-hidden-by-overrides":
                    b.DefaultSource(x => x.Other);
                    b.Node<IQuery>("Query", x => x.Job);
                    b.Node<IProcess>("Process", x => x.Job); break;
            }
        });
        var exception = assemble.Should().Throw<FSMException>().Which;
        exception.Message.Should().ContainAny("Query", "Work.Missing");
        if (scenario == "null-node") exception.Message.Should().Contain("qualified name");
        results.Should().BeEmpty();
    }

    public sealed class WritableInput { public Job Job { get; set; } = new(); }
    public sealed class PrivateInput
    {
        public Job Job { get; private set; } = new();
        public void ReplaceForIdentityProbe(Job replacement) => Job = replacement;
    }
    public sealed class ReadonlyInput { public Job Job { get; } = new(); }
    public sealed class NestedInput { public ReadonlyInput Nested { get; init; } = new(); }

    [Fact]
    public void OrdinaryPublicSetIsRejectedAtAssemblyButStableDeclarationsAreAccepted()
    {
        Action assemble = () => Engine([]).BindContext<WritableInput>();
        assemble.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Query").And.Contain(nameof(IQuery)).And.Contain("Job").And.Contain("public set");
        Engine([]).BindContext<PrivateInput>();
        Engine([]).BindContext<ReadonlyInput>();
        Engine([]).BindContext<Input>(); // init
    }

    private sealed class LyingFactory : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new Query();
        public Type GetNodeType(string key) => typeof(Process);
        public string GetNodeFeatureName(Type type) => "RegisteredProcess";
    }

    [Fact]
    public void FactoryMetadataIsCheckedAgainstTheActualNodeBeforeAssemblyCompletes()
    {
        var engine = new FSMEngine(new LyingFactory());
        engine.CreateNode("RegisteredProcess", "Module.Query");
        Action assemble = () => engine.BindContext<Input>();
        assemble.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Module.Query").And.Contain("RegisteredProcess").And.Contain(nameof(IProcess)).And.Contain(nameof(IQuery));
    }

    public sealed class ThrowingInput { public Job Job => throw new InvalidOperationException("business getter failed"); }

    [Fact]
    public void NullAndDynamicWrongInputsFailWithIdentityAndRootCauseBeforeAnyNodeRuns()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<Input>();
        Action nullSource = () => flow.Prepare(new Input { Job = null! });
        nullSource.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Query").And.Contain(nameof(IQuery)).And.Contain("Job").And.Contain("declared").And.Contain("actual null");
        Action nullAggregate = () => flow.Prepare(null!);
        nullAggregate.Should().Throw<FSMException>().Which.Message.Should().Contain(nameof(Input)).And.Contain("Query");
        Action wrongAggregate = () => flow.PrepareDynamic(new object());
        wrongAggregate.Should().Throw<FSMException>().Which.Message.Should()
            .Contain(nameof(Input)).And.Contain("System.Object").And.Contain("Query").And.Contain(nameof(IQuery)).And.Contain("Job");
        Action throwing = () => Engine(results).BindContext<ThrowingInput>().Prepare(new ThrowingInput());
        var exception = throwing.Should().Throw<FSMException>().Which;
        exception.Message.Should().Contain("Query").And.Contain("Job").And.Contain("business getter failed");
        exception.GetBaseException().Should().BeOfType<InvalidOperationException>();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task PreparationFixesIdentityBeforeFirstNodeAndSharesLiveMemberUpdates()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<PrivateInput>();
        var input = new PrivateInput();
        var original = input.Job;
        var prepared = flow.Prepare(input);
        input.ReplaceForIdentityProbe(new Job { Tasks = "replacement" });
        using var executor = await prepared.StartAsync("Query", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("queried");
        original.Tasks.Should().Be("queried");
        input.Job.Tasks.Should().Be("replacement");
    }

    [Theory]
    [InlineData("convert")]
    [InlineData("conditional")]
    [InlineData("create")]
    [InlineData("call")]
    [InlineData("captured")]
    [InlineData("static")]
    [InlineData("indexer")]
    public void IllegalSourceExpressionsHaveAssemblyDiagnosticsWithNodeAndRequirement(string kind)
    {
        var captured = new Job();
        Action assemble = () => Engine([]).BindContext<ExpressionInput>(b =>
        {
            switch (kind)
            {
                case "convert": b.Node<IQuery>("Query", x => (IQuery)(object)x.Job); break;
                case "conditional": b.Node<IQuery>("Query", x => x.Flag ? x.Job : x.Other); break;
                case "create": b.Node<IQuery>("Query", x => new Job()); break;
                case "call": b.Node<IQuery>("Query", x => x.Pick()); break;
                case "captured": b.Node<IQuery>("Query", x => captured); break;
                case "static": b.Node<IQuery>("Query", x => ExpressionInput.Static); break;
                case "indexer": b.Node<IQuery>("Query", x => x[0]); break;
            }
        });
        assemble.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Query").And.Contain(nameof(IQuery)).And.Contain("Source");
    }
    public sealed class ExpressionInput
    {
        public Job Job { get; init; } = new();
        public Job Other { get; init; } = new();
        public bool Flag => true;
        public Job Pick() => Job;
        public static Job Static => new();
        public Job this[int index] => Job;
    }

    [Fact]
    public async Task ExplicitExistingNestedPathIsAllowedButAutomaticSearchDoesNotRecurse()
    {
        var results = new List<string?>();
        Action automatic = () => Engine(results).BindContext<NestedInput>();
        automatic.Should().Throw<FSMException>().Which.Message.Should().Contain(nameof(IQuery));
        var flow = Engine(results).BindContext<NestedInput>(b => b.DefaultSource(x => x.Nested.Job));
        var input = new NestedInput();
        using var executor = await flow.Prepare(input).StartAsync("Query", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("queried");
    }

    [Fact]
    public void UnreachableNodeSourcesAreStillRequiredWhenStartingAtALocalEntry()
    {
        var results = new List<string?>();
        var engine = Engine(results);
        engine.CreateNode("Process", "Unreachable.Process");
        var flow = engine.BindContext<MultipleInput>(b =>
        {
            b.DefaultSource(x => x.Job);
            b.Node<IProcess>("Unreachable.Process", x => x.Other);
        });
        var input = new MultipleInput { Other = null! };
        Action prepare = () => flow.Prepare(input);
        prepare.Should().Throw<FSMException>().Which.Message.Should().Contain("Unreachable.Process").And.Contain("Other");
        input.Job.Tasks.Should().BeNull();
        results.Should().BeEmpty();
    }

    public sealed class ChangingGetterInput
    {
        private int reads;
        private readonly Job first = new();
        private readonly Job later = new() { Tasks = "wrong identity" };
        public Job Job => reads++ == 0 ? first : later;
    }

    [Fact]
    public async Task MultipleInterfaceViewsOfOneSourceKeepTheSameReferenceEvenWithAnUnstableGetter()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<ChangingGetterInput>();
        using var executor = await flow.Prepare(new ChangingGetterInput()).StartAsync("Query", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("queried");
    }

    [Fact]
    public async Task TypedExecutionCannotBeRestartedThroughAnImplicitLegacyDataEntry()
    {
        var flow = Engine([]).BindContext<Input>();
        using var executor = await flow.Prepare(new Input()).StartAsync("Query", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        Func<Task> restart = async () => await executor.RestartAsync();
        await restart.Should().ThrowAsync<FSMException>();
    }

    [Fact]
    public void ExplicitDuplicateDefaultsAreInvalidEvenIfNoNodeCurrentlyUsesThatInterface()
    {
        var engine = new FSMEngine(new Factory([]));
        Action assemble = () => engine.BindContext<MultipleInput>(b =>
        {
            b.Default<IQuery>(x => x.Job);
            b.Default<IQuery>(x => x.Other);
        });
        assemble.Should().Throw<FSMException>().Which.Message.Should()
            .Contain(nameof(IQuery)).And.Contain("Job").And.Contain("Other");
    }

    public sealed class DeclaredObjectInput { public object Job { get; init; } = new Job(); }
    public sealed class CollectionInput { public Job[] Jobs { get; init; } = [new()]; }
    public sealed class StaticInput { public static Job Job { get; } = new(); }
    public sealed class FieldInput { public readonly Job Job = new(); }
    public sealed class HiddenGetterInput { public Job Job { private get; init; } = new(); }
    public sealed class IndexerInput { public Job this[int index] => new(); }

    [Fact]
    public void AutomaticMatchingUsesOnlyDirectPublicInstanceReadableNonIndexedPropertyDeclarations()
    {
        var engine = Engine([]);
        Action[] invalid = [
            () => engine.BindContext<DeclaredObjectInput>(),
            () => engine.BindContext<CollectionInput>(),
            () => engine.BindContext<StaticInput>(),
            () => engine.BindContext<FieldInput>(),
            () => engine.BindContext<HiddenGetterInput>(),
            () => engine.BindContext<IndexerInput>(),
            () => engine.BindContext<NestedInput>()
        ];
        foreach (var assemble in invalid)
            assemble.Should().Throw<FSMException>().Which.Message.Should().Contain("Query").And.Contain(nameof(IQuery)).And.Contain("found 0");
    }

    [Fact]
    public void AmbiguityListsBothDeclaredSourcesAndChosenNullDoesNotFallBack()
    {
        Action ambiguous = () => Engine([]).BindContext<MultipleInput>();
        ambiguous.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Query").And.Contain(nameof(IQuery)).And.Contain("Job").And.Contain("Other").And.Contain("declared");
        var flow = Engine([]).BindContext<MultipleInput>(b => b.DefaultSource(x => x.Job));
        Action nullDefault = () => flow.Prepare(new MultipleInput { Job = null! });
        nullDefault.Should().Throw<FSMException>().Which.Message.Should().Contain("actual null").And.Contain("Job");
    }

    [Fact]
    public async Task ExplicitInterfaceDefaultOverridesUniqueCompatibleAutomaticSource()
    {
        var results = new List<string?>();
        // Job is the unique direct source; the explicit nested source must win for IProcess only.
        var flow = Engine(results).BindContext<MixedInput>(b => b.Default<IProcess>(x => x.Nested.Job));
        using var executor = await flow.Prepare(new MixedInput()).StartAsync("Query", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("nested");
    }
    public sealed class MixedInput
    {
        public Job Job { get; init; } = new();
        public Input Nested { get; init; } = new() { Job = new Job { Tasks = "nested" } };
    }

    [Fact]
    public async Task LegacyDataCannotSupplyATypedNodesBusinessDependency()
    {
        var results = new List<string?>();
        var engine = Engine(results);
        using var executor = new FSMExecutor(engine.GetNode("Query"), engine.GetEvent("Done"));
        Func<Task> start = async () => await executor.RestartAsync(new Job());
        await start.Should().ThrowAsync<FSMException>();
        results.Should().BeEmpty();
    }

    [Fact]
    public void APlanCannotSilentlySkipNodesAddedAfterWholeEngineAssembly()
    {
        var engine = Engine([]);
        var flow = engine.BindContext<Input>();
        engine.CreateNode("Process", "Later.Process");
        Action prepare = () => flow.Prepare(new Input());
        prepare.Should().Throw<FSMException>().Which.Message.Should().Contain("Later.Process").And.Contain("BindContext");
    }

    public struct StructBusiness : IQuery, IProcess { public string? Tasks { get; set; } }
    public sealed class StructInput { public StructBusiness Job { get; init; } }

    [Fact]
    public void ValueTypeSourcesCannotSilentlyTurnBorrowingIntoABoxedBusinessCopy()
    {
        Action assemble = () => Engine([]).BindContext<StructInput>();
        assemble.Should().Throw<FSMException>().Which.Message.Should().Contain("Query").And.Contain("Job").And.Contain("reference");
    }

    private sealed class NoBusiness : BaseFSMNode
    {
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }
    private sealed class InvalidMetadataFactory(bool missing) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => new NoBusiness();
        public Type GetNodeType(string key) => missing ? null! : typeof(object);
        public string GetNodeFeatureName(Type type) => "Invalid";
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrNonNodeFactoryMetadataCannotMasqueradeAsNoBusinessDependency(bool missing)
    {
        var engine = new FSMEngine(new InvalidMetadataFactory(missing));
        engine.CreateNode("Invalid", "Work.NoBusiness");
        Action assemble = () => engine.BindContext<Input>();
        assemble.Should().Throw<FSMException>().Which.Message.Should().Contain("Work.NoBusiness").And.Contain("metadata");
    }

    private sealed class Misreported : BaseFSMNode<IQuery>, IFSMContextNode
    {
        Type? IFSMContextNode.ContextType => typeof(IProcess);
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }
    private sealed class MultipleRequirements : BaseFSMNode<IQuery>, IFSMContextNode<IProcess>
    {
        IProcess IFSMContextNode<IProcess>.Context => new Job();
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }
    private sealed class RequirementFactory(bool multiple) : IFSMNodeFactory
    {
        public IFSMNode CreateNode(string key) => multiple ? new MultipleRequirements() : new Misreported();
        public Type GetNodeType(string key) => multiple ? typeof(MultipleRequirements) : typeof(Misreported);
        public string GetNodeFeatureName(Type type) => "Requirement";
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualGenericRequirementAndReportedMetadataMustAgreeWithDiagnostics(bool multiple)
    {
        var engine = new FSMEngine(new RequirementFactory(multiple));
        engine.CreateNode("Requirement", "Work.Query");
        Action assemble = () => engine.BindContext<Input>();
        assemble.Should().Throw<FSMException>().Which.Message.Should()
            .Contain("Work.Query").And.Contain(nameof(IQuery)).And.Contain(nameof(IProcess));
    }

    public sealed class QueryViewOnlyInput { public IQuery Job { get; init; } = new Job(); }

    [Fact]
    public void AnUnreachableActualRequirementIsNotExemptAndRuntimeExtraInterfacesAreNotGuessed()
    {
        var engine = Engine([]);
        engine.ClearTransition("Query"); // Process is unreachable from the intended local entry.
        Action assemble = () => engine.BindContext<QueryViewOnlyInput>();
        assemble.Should().Throw<FSMException>().Which.Message.Should().Contain("Process").And.Contain(nameof(IProcess));
    }

    [Fact]
    public async Task ValidBusinessObjectsWithUnreadyFieldsAreHandledByBusinessNodesNotTheBinder()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<Input>();
        using var executor = await flow.Prepare(new Input()).StartAsync("Process", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal(new string?[] { null });
    }

    [Fact]
    public async Task AnAssembledEntryNeedsOnlyOneAggregateInstanceForEachExecution()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<Input>("Query", "Done");
        using var first = await flow.Prepare(new Input()).StartAsync();
        await first.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        using var second = await flow.Prepare(new Input()).StartAsync();
        await second.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("queried", "queried");
    }

    [Fact]
    public async Task NewExecutionUsesTheNewExplicitAggregateRatherThanResidualBusinessBindings()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<MultipleInput>("Query", "Done", b =>
        {
            b.DefaultSource(x => x.Job);
            b.Node<IProcess>("Process", x => x.Other);
        });
        var firstInput = new MultipleInput { Other = new Job { Tasks = "first run" } };
        var secondInput = new MultipleInput { Other = new Job { Tasks = "second run" } };
        using (var first = await flow.Prepare(firstInput).StartAsync())
            await first.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        using (var second = await flow.Prepare(secondInput).StartAsync())
            await second.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("first run", "second run");
        firstInput.Other.Tasks.Should().Be("first run");
        secondInput.Other.Tasks.Should().Be("second run");
    }

    public sealed class QuerySource : IQuery { public string? Tasks { get; set; } }
    public sealed class ProcessSource : IProcess { public string? Tasks => "independent process source"; }
    public class HiddenSourceBaseInput { public QuerySource Source { get; } = new(); }
    public sealed class HiddenSourceInput : HiddenSourceBaseInput { public new ProcessSource Source { get; } = new(); }

    [Fact]
    public async Task HiddenSameNameSourceDeclarationsRemainIndependentThroughoutExecution()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<HiddenSourceInput>("Query", "Done");
        var input = new HiddenSourceInput();
        var prepared = flow.Prepare(input);
        using var executor = await prepared.StartAsync();
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("independent process source");
        HiddenSourceBaseInput baseInput = input;
        baseInput.Source.Tasks.Should().Be("queried");
    }

    public class InheritedSourceBase<T> where T : class, new()
    {
        public int SourceReads { get; private set; }
        public T Source { get { SourceReads++; return new T(); } }
    }
    public sealed class InheritedSourceInput : InheritedSourceBase<Job> { }

    [Theory]
    [InlineData("automatic")]
    [InlineData("interface-default")]
    [InlineData("node")]
    [InlineData("default-source")]
    [InlineData("declaring-property")]
    public async Task SameInheritedGenericPropertyIsEvaluatedOnceAcrossAutomaticAndExplicitRules(string rule)
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<InheritedSourceInput>("Query", "Done", b =>
        {
            switch (rule)
            {
                case "interface-default": b.Default<IProcess>(x => x.Source); break;
                case "node": b.Node<IProcess>("Process", x => x.Source); break;
                case "default-source": b.DefaultSource(x => x.Source); break;
                case "declaring-property":
                    // A public selector built from the declaring base's PropertyInfo;
                    // automatic scanning reflects that same property through the derived input.
                    var parameter = Expression.Parameter(typeof(InheritedSourceInput), "input");
                    var property = typeof(InheritedSourceBase<Job>).GetProperty(nameof(InheritedSourceInput.Source))!;
                    b.Default<IProcess>(Expression.Lambda<Func<InheritedSourceInput, IProcess>>(
                        Expression.Property(parameter, property), parameter));
                    break;
            }
        });
        var input = new InheritedSourceInput();
        var prepared = flow.Prepare(input);
        input.SourceReads.Should().Be(1);
        using var executor = await prepared.StartAsync();
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("queried");
        input.SourceReads.Should().Be(1);
    }

    public sealed class NestedIdentityInput
    {
        public Input Left { get; } = new();
        public Input Right { get; } = new() { Job = new Job { Tasks = "right source" } };
    }

    [Fact]
    public async Task DifferentOuterPathsToTheSameTerminalDeclarationKeepIndependentBusinessObjects()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<NestedIdentityInput>("Query", "Done", b =>
        {
            b.DefaultSource(x => x.Left.Job);
            b.Node<IProcess>("Process", x => x.Right.Job);
        });
        var input = new NestedIdentityInput();
        using var executor = await flow.Prepare(input).StartAsync();
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("right source");
        input.Left.Job.Tasks.Should().Be("queried");
        input.Right.Job.Tasks.Should().Be("right source");
    }

    public sealed class GenericSourceHolder<T> where T : class, new() { public T Source { get; } = new(); }
    public sealed class ConstructedGenericPathsInput
    {
        public GenericSourceHolder<QuerySource> Query { get; } = new();
        public GenericSourceHolder<ProcessSource> Process { get; } = new();
    }

    [Fact]
    public async Task ExplicitPathsThroughDifferentConstructedGenericMembersKeepTheirDeclaredSources()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<ConstructedGenericPathsInput>("Query", "Done", b =>
        {
            b.Default<IQuery>(x => x.Query.Source);
            b.Default<IProcess>(x => x.Process.Source);
        });
        var input = new ConstructedGenericPathsInput();
        using var executor = await flow.Prepare(input).StartAsync();
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("independent process source");
        input.Query.Source.Tasks.Should().Be("queried");
    }

    [Fact]
    public async Task LocalInterfacesShareTheAggregateMemberThroughRealExecution()
    {
        var results = new List<string?>();
        var flow = Engine(results).BindContext<Input>();
        var input = new Input();
        var prepared = flow.Prepare(input);
        using var executor = await prepared.StartAsync("Query", "Done");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().Equal("queried");
        input.Job.Tasks.Should().Be("queried");
    }
}
