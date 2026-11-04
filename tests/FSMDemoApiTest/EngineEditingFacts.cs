using FSMDemo.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace FSMDemoApiTest;

public class EngineEditingFacts
{
    private const string Modules = """
        module Choice {
            input Entry, Retry; output Done, Unused;
            def Entry(Step) { 1->Next; 2->Done; 3->Done; }
            def Retry(Step) { 1->Done; }
            def Private(Step) { 1->Next; }
            Next -> Entry to Private;
        }
        module Single { input Entry; output Done; def Entry(Step) { 1->Done; } }
        def Start(Step) { 1->Launch; }
        def End(Step) {} def Other(Step) {}
        use Choice as A;
        use Single as B;
        """;

    private static EngineManager Create(string script = Modules)
    {
        var manager = new EngineManager(NullLogger<EngineManager>.Instance, new ScriptNodeFactory());
        manager.Import(script);
        return manager;
    }

    private sealed class GroupFactory : IFSMNodeFactory
    {
        private readonly ScriptNodeFactory _steps = new();
        public IFSMNode CreateNode(string featureName) => featureName switch
        {
            "Group" => new GroupNode(), "Parallel" => new ParallelNode(), _ => _steps.CreateNode(featureName),
        };
        public Type GetNodeType(string featureName) => featureName switch
        {
            "Group" => typeof(GroupNode), "Parallel" => typeof(ParallelNode), _ => _steps.GetNodeType(featureName),
        };
        public string GetNodeFeatureName(Type type) => type == typeof(GroupNode) ? "Group" : type == typeof(ParallelNode) ? "Parallel" : _steps.GetNodeFeatureName(type);
    }

    private static string[] Branches(EngineManager manager, string node)
    {
        var info = manager.GetNode(node)!;
        return info.EventDescriptions.Select(b => $"{b.Index}:{b.Description}").ToArray();
    }

    [Fact]
    public void GraphCreation_UsesQualifiedEventsAndNoRuntimeProxyOrOutputMapping()
    {
        var manager = Create();
        var dto = manager.AddModuleInstance("Choice", "Area.C", 12, 34);
        var instance = manager.Engine.ModuleInstances["Area.C"];
        instance.OutputEventMap.Should().BeEmpty();
        manager.Engine.ContainsNode("Area.C").Should().BeFalse();
        manager.Engine.ContainsEvent("Area.C.Done").Should().BeTrue();
        manager.Engine.ContainsEvent("Area.C_Done").Should().BeFalse();
        instance.PublicInputs.Should().Contain("Retry", "Area.C.Retry");
        instance.PublicOutputs.Should().Contain("Done", "Area.C.Done");
        instance.ExpandedNodeNames.Should().BeEquivalentTo("Area.C.Entry", "Area.C.Retry", "Area.C.Private");
        dto.PosX.Should().Be(12); dto.PosY.Should().Be(34);
        manager.Engine.ModuleInterfaces.ResolveOutput("Area.C.Done").Publishers.Select(n => n.Name)
            .Should().BeEquivalentTo("Area.C.Entry", "Area.C.Retry");
        Branches(manager, "Area.C.Entry").Should().Contain("1:Area.C.Next");
    }

    [Fact]
    public void OutputWiring_ConnectsAllActualPublishersWithoutRebindingBranches_AndProjectsOnce()
    {
        var manager = Create();
        var before = Branches(manager, "A.Entry");
        manager.CreateConnection("A", "B", "A.Done").Should().Be((true, ""));
        foreach (var publisher in new[] { "A.Entry", "A.Retry" })
            manager.Engine[publisher].TargetState(manager.Engine.GetEvent("A.Done")).Name.Should().Be("B.Entry");
        Branches(manager, "A.Entry").Should().Equal(before);
        manager.GetConnections().Count(c => c.FromNodeName == "A" && c.ToNodeName == "B" && c.EventName == "A.Done")
            .Should().Be(1);
        manager.DeleteConnection("A", "B", "A.Done").Should().BeTrue();
        foreach (var publisher in new[] { "A.Entry", "A.Retry" })
            manager.Engine[publisher].HasTransition(manager.Engine.GetEvent("A.Done")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Start", "A", "Launch")]
    [InlineData("Start", "A.Private", "Launch")]
    [InlineData("Start", "A.Missing", "Launch")]
    [InlineData("A", "End", "A.Next")]
    [InlineData("A", "End", "A.Unused")]
    [InlineData("A", "End", "Done")]
    [InlineData("A.Private", "End", "A.Next")]
    public void InvalidPublicWiring_IsRejectedWithoutMutatingGraph(string source, string target, string ev)
    {
        var manager = Create();
        var before = manager.GetConnections().Count;
        var result = manager.CreateConnection(source, target, ev);
        result.Success.Should().BeFalse(); result.Error.Should().NotBeNullOrEmpty();
        manager.GetConnections().Should().HaveCount(before);
    }

    [Fact]
    public void MultipleInputs_KeepSelectedInputIdentityInProjectionAndDeletion()
    {
        var manager = Create();
        manager.CreateConnection("Start", "A.Retry", "Launch").Success.Should().BeTrue();
        manager.CreateConnection("B", "A.Entry", "B.Done").Success.Should().BeTrue();
        manager.GetConnections().Should().Contain(c => c.FromNodeName == "Start" && c.ToNodeName == "A.Retry");
        manager.GetConnections().Should().Contain(c => c.FromNodeName == "B" && c.ToNodeName == "A.Entry");
        manager.DeleteConnection("Start", "A.Entry", "Launch").Should().BeFalse();
        manager.DeleteConnection("Start", "A.Retry", "Launch").Should().BeTrue();
    }

    [Fact]
    public void ConflictingPublisher_IsRejectedAtomically()
    {
        var manager = Create();
        manager.Engine["A.Retry"].AddTransition(manager.Engine.GetEvent("A.Done"), manager.Engine["Other"]);
        manager.CreateConnection("A", "End", "A.Done").Success.Should().BeFalse();
        manager.Engine["A.Entry"].HasTransition(manager.Engine.GetEvent("A.Done")).Should().BeFalse();
        manager.Engine["A.Retry"].TargetState(manager.Engine.GetEvent("A.Done")).Name.Should().Be("Other");
    }

    [Fact]
    public void TemplateRebuild_PreservesPublicEdgesAcrossAllInstancesAndRefreshesPublishers()
    {
        var manager = Create();
        manager.AddModuleInstance("Choice", "C", 4, 5);
        manager.CreateConnection("Start", "A.Retry", "Launch").Success.Should().BeTrue();
        manager.CreateConnection("A", "B", "A.Done").Success.Should().BeTrue();
        manager.CreateConnection("C", "End", "C.Done").Success.Should().BeTrue();
        var flowId = manager.Engine.ModuleInstances["C"].FlowID;
        manager.UpdateNodePosition("A.Entry", 45, 67).Should().BeTrue();
        manager.UpdateNodeEvent("A.Private", 1, "A.Done").Should().BeTrue();
        manager.Engine["C.Entry"].PosX.Should().Be(45);
        manager.Engine.ModuleInstances["C"].FlowID.Should().Be(flowId);
        manager.Engine.ModuleInstances["C"].PosX.Should().Be(4);
        manager.Engine["Start"].TargetState(manager.Engine.GetEvent("Launch")).Name.Should().Be("A.Retry");
        foreach (var publisher in new[] { "Entry", "Retry", "Private" })
        {
            manager.Engine["A." + publisher].TargetState(manager.Engine.GetEvent("A.Done")).Name.Should().Be("B.Entry");
            manager.Engine["C." + publisher].TargetState(manager.Engine.GetEvent("C.Done")).Name.Should().Be("End");
        }
        manager.GetConnections().Count(c => c.FromNodeName == "A" && c.EventName == "A.Done").Should().Be(1);
        manager.Engine.ModuleInstances["A"].OutputEventMap.Should().BeEmpty();
    }

    [Fact]
    public void ObsoleteTerminalAndInstanceEventRename_AreDisabled()
    {
        var manager = Create();
        manager.UpdateModuleTerminal("A.Private", true).Should().BeFalse();
        manager.UpdateNodeEvent("A", 0, "Renamed").Should().BeFalse();
        manager.CreateConnection("A", "End", "A.Done").Success.Should().BeTrue();
        manager.RenameConnection("A", "End", "Renamed", "A.Done").Should().BeFalse();
        manager.RenameConnection("A.Entry", "End", "Renamed", "A.Done").Should().BeFalse();
        manager.Engine.ModuleInstances["A"].OutputEventMap.Should().BeEmpty();
    }

    [Fact]
    public void OrdinaryNodes_KeepCreateRenameDeleteAndBranchEditingBehavior()
    {
        var manager = Create();
        manager.CreateNode("Step", "Plain", 8, 9);
        manager.UpdateNodeEvent("Plain", 1, "Go").Should().BeTrue();
        manager.CreateConnection("Plain", "End", "Go").Success.Should().BeTrue();
        manager.RenameConnection("Plain", "End", "Changed", "Go").Should().BeTrue();
        manager.DeleteConnection("Plain", "End", "Changed").Should().BeTrue();
        manager.RenameNode("Plain", "Renamed").Should().BeTrue();
        manager.DeleteNode("Renamed").Should().BeTrue();
    }

    [Fact]
    public void PublicInterfaceDto_ReportsAllPublishersAndUnusedOutputs()
    {
        var manager = Create();
        var dto = manager.GetModuleInstances().Single(i => i.InstanceName == "A");
        dto.PublicInputs.Should().Contain("Entry", "A.Entry").And.Contain("Retry", "A.Retry");
        dto.PublicOutputs.Should().Contain("Done", "A.Done");
        dto.OutputPublishers["Done"].Should().BeEquivalentTo("A.Entry", "A.Retry");
        dto.OutputPublishers["Unused"].Should().BeEmpty();
        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        json.Should().Contain("\"publicInputs\"").And.Contain("\"publicOutputs\"").And.Contain("\"outputPublishers\"");
        json.Should().NotContain("terminalNodeNames").And.NotContain("outputEventMap");
    }

    [Fact]
    public void InternalTemplateEditing_PreservesExternalEdgesAndRejectsSourceEventConflicts()
    {
        var manager = Create();
        manager.CreateConnection("A", "B", "A.Done").Success.Should().BeTrue();
        manager.CreateConnection("A.Entry", "A.Retry", "A.Next").Success.Should().BeFalse();
        manager.DeleteConnection("A.Entry", "A.Private", "A.Next").Should().BeTrue();
        manager.CreateConnection("A.Entry", "A.Retry", "A.Next").Success.Should().BeTrue();
        manager.RenameConnection("A.Entry", "A.Retry", "A.Again", "A.Next").Should().BeTrue();
        manager.Engine["A.Entry"].TargetState(manager.Engine.GetEvent("A.Again")).Name.Should().Be("A.Retry");
        manager.Engine["A.Retry"].TargetState(manager.Engine.GetEvent("A.Done")).Name.Should().Be("B.Entry");
    }

    [Fact]
    public void PublicSelfLoop_IsProjectedAndPreservedAcrossTemplateRebuild()
    {
        var manager = Create();
        manager.CreateConnection("B", "B", "B.Done").Success.Should().BeTrue();
        manager.GetConnections().Should().Contain(c => c.FromNodeName == "B" && c.ToNodeName == "B" && c.EventName == "B.Done");
        manager.UpdateNodePosition("B.Entry", 2, 3).Should().BeTrue();
        manager.Engine["B.Entry"].TargetState(manager.Engine.GetEvent("B.Done")).Name.Should().Be("B.Entry");
    }

    [Fact]
    public void RemovingLastPublisherOfAConnectedOutput_IsRejectedWithoutLosingConnection()
    {
        var manager = Create();
        manager.CreateConnection("B", "End", "B.Done").Success.Should().BeTrue();
        manager.UpdateNodeEvent("B.Entry", 1, "B.Other").Should().BeFalse();
        Branches(manager, "B.Entry").Should().Contain("1:B.Done");
        manager.Engine["B.Entry"].TargetState(manager.Engine.GetEvent("B.Done")).Name.Should().Be("End");
        manager.Engine.TryGetModule("Single", out var template).Should().BeTrue();
        template.TemplateNodes["Entry"].UpdateEventDescriptions();
        template.TemplateNodes["Entry"].EventDescriptions.Should().Contain(b => b.Description == "Done");
    }

    [Fact]
    public void NamespaceMetadata_OnExpandedNodesSurvivesTemplateRebuild()
    {
        var manager = Create("module M { input Entry; output Done; def Entry(Step) { 1->Done; } } namespace N { use M as A; }");
        manager.UpdateNodePosition("N.A.Entry", 2, 3).Should().BeTrue();
        manager.Engine["N.A.Entry"].NamePrefix.Should().Be("N.");
    }

    [Theory]
    [InlineData("Start")]
    [InlineData("A")]
    [InlineData("A.Entry")]
    public void InstanceNameCollisions_AreRejectedWithoutPartialInstallation(string instanceName)
    {
        var manager = Create();
        var before = manager.Engine.GetNodeNames().ToArray();
        Action add = () => manager.AddModuleInstance("Single", instanceName, 0, 0);
        add.Should().Throw<InvalidOperationException>();
        manager.Engine.GetNodeNames().Should().BeEquivalentTo(before);
    }

    [Fact]
    public void LazyRegisteredModule_IsLoadedByCoreBeforeGraphCreation()
    {
        var manager = Create();
        manager.Engine.RegisterModule("Lazy", "module Lazy { input Entry; output Done; def Entry(Step) { 1->Done; } }");
        var dto = manager.AddModuleInstance("Lazy", "New", 1, 2);
        dto.PublicInputs.Should().Contain("Entry", "New.Entry");
        manager.Engine["New.Entry"].UpdateEventDescriptions();
        manager.Engine.TryGetModule("Lazy", out var template).Should().BeTrue();
        template.TemplateNodes.Should().ContainKey("Entry");
        manager.UpdateNodePosition("New.Entry", 4, 5).Should().BeTrue();
        manager.Engine.ModuleInterfaces.ResolveOutput("New.Done").Publishers.Should().HaveCount(1);
    }

    [Fact]
    public void InternalOutputConflict_IsRejectedBeforeRebuildAndDoesNotLeakAnEvent()
    {
        var manager = Create();
        manager.CreateConnection("A", "End", "A.Done").Success.Should().BeTrue();
        var before = manager.Engine.GetEventNames().ToArray();
        manager.CreateConnection("A.Entry", "A.Retry", "A.Done").Success.Should().BeFalse();
        manager.Engine["A.Entry"].TargetState(manager.Engine.GetEvent("A.Done")).Name.Should().Be("End");
        manager.Engine.GetEventNames().Should().BeEquivalentTo(before);
    }

    [Fact]
    public void GroupAndParallelTemplateEditing_ReusesCoreQualificationAndPreservesPublicEdges()
    {
        var manager = new EngineManager(NullLogger<EngineManager>.Instance, new GroupFactory());
        manager.Import("""
            module M {
                input Entry; output Done;
                def Entry(Step) { 1->Done; }
                def Retry(Step) { 1->Done; }
                def G(Group) { [Entry->Done]; }
                def P(Parallel) { [Entry->Done]; }
            }
            use M as A; use M as B;
            def End(Step) {}
            A.Done -> End;
            def Ordinary(Group) { [End->Finish]; }
            """);
        manager.UpdateGroupDefs("A.G", [new() { StartNode = "A.Retry", EndEvent = "A.Done" }]).Should().BeTrue();
        manager.UpdateGroupDefs("A.P", [new() { StartNode = "A.Retry", EndEvent = "A.Done" }]).Should().BeTrue();
        ((GroupNode)manager.Engine["B.G"]).StartNode.Should().Be("B.Retry");
        ((ParallelNode)manager.Engine["B.P"]).FSMs.Single().EndEvent.Should().Be("B.Done");
        manager.Engine["A.Retry"].TargetState(manager.Engine.GetEvent("A.Done")).Name.Should().Be("End");
        manager.UpdateGroupDefs("Ordinary", [new() { StartNode = "End", EndEvent = "Finish" }]).Should().BeTrue();
        manager.GetNode("Ordinary")!.GroupDefs!.Single().EndEvent.Should().Be("Finish");
    }

    [Fact]
    public void TerminalController_ReturnsExplicitBadRequest()
    {
        var controller = new FSMDemo.API.Controllers.NodesController(Create());
        var result = controller.UpdateModuleTerminal("A.Private", new FSMDemo.API.Controllers.UpdateModuleTerminalRequest { IsTerminal = true });
        result.Should().BeOfType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>();
    }

    [Fact]
    public void OrdinaryDottedNodes_AreNotTreatedAsModuleMembersOrDeletedWithInstance()
    {
        var manager = Create(Modules + "def A.Other(Step) { 1->Plain; }");
        manager.CreateConnection("A.Other", "End", "Plain").Success.Should().BeTrue();
        manager.UpdateNodePosition("A.Other", 1, 2).Should().BeTrue();
        manager.DeleteModuleInstance("A").Should().BeTrue();
        manager.Engine.ContainsNode("A.Other").Should().BeTrue();
    }
}
