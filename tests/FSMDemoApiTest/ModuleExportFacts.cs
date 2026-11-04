using FSMDemo.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using StateMachine;
using StateMachineTest.TestInfrastructure;

namespace FSMDemoApiTest;

public class ModuleExportFacts
{
    private static EngineManager CreateManager() => new(NullLogger<EngineManager>.Instance, new ScriptNodeFactory());

    [Fact]
    public void ImportExport_RoundTripsModuleInterfacesConnectionsAndDisplayMetadata()
    {
        var manager = CreateManager();
        manager.Import("""
            module Choice {
                input Entry, Retry; output Done, Unused;
                def Entry(Step) { 1->Next; 2->Done; 3->Done; Pos:(4,5); Color:"green"; FlowID:entry_id; }
                def Retry(Step) { 1->Done; }
                def Private(Step) { 1->Done; }
                Next -> Entry to Private;
            }
            namespace Area {
                use Choice as Left { Pos:(12.5,-3); Color:"red"; FlowID:left_id; }
                use Choice as Right { Pos:(32,17); Color:"blue"; FlowID:right_id; }
                def Start(Step) { 1->Launch; Pos:(1,2); Color:"yellow"; FlowID:start_id; }
                def Unused.Event event;
                Launch -> Start to Left.Retry;
                Left.Done -> Right.Entry;
            }
            def End(Step) { FlowID:end_id; }
            Area.Right.Done -> End;
            """);

        var script = manager.Export();
        script.Should().Contain("use Choice as ").And.Contain("Area.Left.Done -> Area.Right.Entry;");
        script.Should().NotContain("terminal").And.NotContain("import Choice").And.NotContain("def Area.Left(");
        script.Split("Area.Left.Done -> Area.Right.Entry;").Length.Should().Be(2, "多个发布者和 Branch 只导出一条接口边");
        script.Should().NotContain("Area.Left.Private to");

        var restored = CreateManager();
        restored.Import(script);
        AssertEquivalent(manager.Engine, restored.Engine);
        restored.Engine.GetNode("Area.Start").NamePrefix.Should().Be("Area.");
        restored.Engine.GetNode("Area.Left.Entry").NamePrefix.Should().Be("Area.", "实例所在 Namespace 不能在往返中消失");
        restored.Engine.ModuleInstances["Area.Left"].FlowID.Should().Be("left_id");
        restored.Engine.ModuleInstances["Area.Left"].PosX.Should().Be(12.5);
        restored.Engine.ModuleInstances["Area.Left"].Color.Should().Be("red");
        restored.Engine.TryGetModule("Choice", out var restoredTemplate).Should().BeTrue();
        restoredTemplate.TemplateNodes["Entry"].FlowID.Should().Be("entry_id");
        restored.Engine.GetEvent("Area.Left.Done").Should().NotBeSameAs(restored.Engine.GetEvent("Area.Right.Done"));
    }

    [Fact]
    public void Export_RejectsAnInstanceWhoseInternalConnectionsNoLongerMatchItsTemplate()
    {
        var manager = CreateManager();
        manager.Import("""
            module M {
                input Entry; output Done;
                def Entry(Step) { 1->Next; }
                def Exit(Step) { 1->Done; }
                Next -> Entry to Exit;
            }
            use M as A;
            """);
        manager.Engine.GetNode("A.Entry").DeleteTransition(manager.Engine.GetEvent("A.Next"));
        Action export = () => manager.Export();
        export.Should().Throw<InvalidOperationException>().WithMessage("*A*模板*");
    }

    [Fact]
    public void Export_RejectsPerInstanceBranchChangesInsteadOfSilentlyRestoringTemplateBindings()
    {
        var manager = CreateManager();
        manager.Import("module M { input Entry; output Done; def Entry(Step) { 1->Done; } } use M as A;");
        manager.Engine.AttachEvent("A.Entry", "LegacyAlias", 1);
        Action export = () => manager.Export();
        export.Should().Throw<InvalidOperationException>().WithMessage("*A*Branch*");
    }

    [Fact]
    public void GraphEditing_ExportsNewInstancesPublicEdgesAndTemplateChangesForReimport()
    {
        var manager = CreateManager();
        manager.Import("""
            module M { input Entry; output Done; def Entry(Step) { 1->Done; } }
            def Start(Step) { 1->Launch; } def End(Step) {}
            def A.Other(Step) { 1->OtherEvent; }
            """);
        manager.AddModuleInstance("M", "A", 2, 3);
        manager.AddModuleInstance("M", "B", 12, 13);
        manager.CreateConnection("Start", "A", "Launch").Success.Should().BeTrue();
        manager.CreateConnection("A", "B", "A.Done").Success.Should().BeTrue();
        manager.CreateConnection("B", "End", "B.Done").Success.Should().BeTrue();
        manager.CreateConnection("A.Other", "End", "OtherEvent").Success.Should().BeTrue();
        manager.UpdateNodePosition("A.Entry", 10, 20).Should().BeTrue();
        manager.UpdateModuleInstancePosition("B", 42, 43).Should().BeTrue();
        var script = manager.Export();
        script.Should().Contain("A.Done -> B;").And.Contain("OtherEvent -> A.Other to End;");
        var restored = CreateManager();
        restored.Import(script);
        AssertEquivalent(manager.Engine, restored.Engine);
        restored.Engine.ModuleInstances["B"].PosX.Should().Be(42);
        restored.Engine.GetNode("B.Entry").PosX.Should().Be(10);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("partial-publishers")]
    [InlineData("private-target")]
    public void Export_RejectsGraphsThatCannotBeExpressedByThePublicInterfaceContract(string graphKind)
    {
        var manager = CreateManager();
        manager.Import("""
            module M {
                input Entry; output Done;
                def Entry(Step) { 1->Done; }
                def Other(Step) { 1->Done; }
            }
            use M as A;
            def Start(Step) { 1->Launch; } def End(Step) {}
            """);
        switch (graphKind)
        {
            case "mapping": manager.Engine.ModuleInstances["A"].OutputEventMap["Done"] = "Alias"; break;
            case "partial-publishers": manager.Engine.ConnectNode("A.Done", "A.Entry", "End"); break;
            case "private-target": manager.Engine.ConnectNode("Launch", "Start", "A.Other"); break;
        }
        Action export = () => manager.Export();
        export.Should().Throw<InvalidOperationException>().WithMessage("*无法*导出*");
    }

    [Fact]
    public void NamespaceOrdinaryBranchEditing_RoundTripsRelativeAndQualifiedEventNames()
    {
        var manager = CreateManager();
        manager.Import("""
            module M { input Entry; def Entry(Step) {} }
            namespace N { def Start(Step) { 1->Launch; } }
            use M as A;
            """);
        manager.UpdateNodeEvent("N.Start", 1, "Other").Should().BeTrue();
        var restored = CreateManager();
        restored.Import(manager.Export());
        restored.GetNode("N.Start")!.EventDescriptions.Single().Description.Should().Be("N.Other");
        restored.UpdateNodeEvent("N.Start", 1, "N.Qualified").Should().BeTrue();
        manager.Import(restored.Export());
        manager.GetNode("N.Start")!.EventDescriptions.Single().Description.Should().Be("N.Qualified");
        manager.Engine.GetEventNames().Should().NotContain("Other").And.NotContain("N.N.Qualified");
    }

    private static void AssertEquivalent(FSMEngine original, FSMEngine restored)
    {
        restored.GetNodeNames().Should().BeEquivalentTo(original.GetNodeNames());
        restored.GetEventNames().Should().BeEquivalentTo(original.GetEventNames());
        foreach (var name in original.GetNodeNames())
        {
            var before = original.GetNode(name);
            var after = restored.GetNode(name);
            after.ClassType.Should().Be(before.ClassType);
            (after.PosX, after.PosY, after.Color).Should().Be((before.PosX, before.PosY, before.Color));
            if (!original.ModuleInstances.Values.Any(i => i.ExpandedNodeNames.Contains(name)))
                after.FlowID.Should().Be(before.FlowID, "普通节点显式标识应保存；展开节点的自动标识不比较");
            before.UpdateEventDescriptions();
            after.UpdateEventDescriptions();
            after.EventDescriptions.Select(b => (b.Index, b.Description))
                .Should().BeEquivalentTo(before.EventDescriptions.Select(b => (b.Index, b.Description)));
            after.GetFSMTransitions().Select(t => (t.Trigger.EventName, t.Target.Name))
                .Should().BeEquivalentTo(before.GetFSMTransitions().Select(t => (t.Trigger.EventName, t.Target.Name)));
        }
        foreach (var (name, instance) in original.ModuleInstances)
        {
            restored.ModuleInstances[name].PublicInputs.Should().BeEquivalentTo(instance.PublicInputs);
            restored.ModuleInstances[name].PublicOutputs.Should().BeEquivalentTo(instance.PublicOutputs);
        }
    }
}
