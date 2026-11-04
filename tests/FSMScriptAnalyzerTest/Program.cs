using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using StateMachine;

namespace FSMScriptAnalyzerTest;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            var factory = new ReflectionNodeFactory(new[]
            {
                Assembly.GetExecutingAssembly(), typeof(StartNode).Assembly, typeof(IFSMNode).Assembly
            });
            SameFileModules(factory);
            PlainScriptScopes(factory);
            ExistingPlainScript(factory);
            EmbeddedModulesAndNamespace(factory);
            RelativeAdditionalFiles(factory);
            var inline = InlineModules.Create(factory);
            await ExecutesToFinish(inline.Start, inline.Finished,
                "Start", "First.Entry", "First.Exit", "Second.Entry", "Second.Exit", "Finish");
            var embedded = EmbeddedModules.Create(factory);
            await ExecutesToFinish(embedded.Area_Start, embedded.Area_Finished,
                "Area.Start", "Area.One.Retry", "Area.Two.Entry", "Area.Two.Private", "Area.Finish");
            Console.WriteLine("PASS: generated consumers compiled, initialized and queried successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task ExecutesToFinish(IFSMNode start, FSMEvent end, params string[] expectedNodes)
    {
        using var executor = new FSMExecutor(start, end);
        var visited = new System.Collections.Concurrent.ConcurrentQueue<string>();
        executor.NodeStateChanged += (_, name) => visited.Enqueue(name);
        Check(await executor.RestartAsync(true).WaitAsync(TimeSpan.FromSeconds(5)), "generated flow starts");
        await executor.ExecutorTask.WaitAsync(TimeSpan.FromSeconds(5));
        Check(executor.State == FSMState.Finished, "generated flow reaches its end event");
        foreach (var name in expectedNodes)
            Check(visited.Contains(name), "generated flow executes real node: " + name);
    }

    private static void SameFileModules(IFSMNodeFactory factory)
    {
        var flow = InlineModules.Create(factory);
        var engine = flow.Engine;
        AssertRealAccessors(flow, engine);
        Same(flow.Start, engine.GetNode("Start"), "generated Start accessor");
        Same(flow.Launch, engine.GetEvent("Launch"), "generated Launch accessor");
        Same(((IFSMNode)flow.Start).TargetState(flow.Launch), engine.GetNode("First.Entry"), "single public Input");
        Same(engine.GetNode("First.Exit").TargetState(engine.GetEvent("First.Done")),
            engine.GetNode("Second.Entry"), "Output connects next Module Instance");
        Same(engine.GetNode("Second.Exit").TargetState(engine.GetEvent("Second.Done")),
            flow.Finish, "Output connects ordinary node");
        Check(!ReferenceEquals(engine.GetNode("First.Entry"), engine.GetNode("Second.Entry")), "node isolation");
        Check(!ReferenceEquals(engine.GetEvent("First.Done"), engine.GetEvent("Second.Done")), "event isolation");
        Check(!engine.GetNodeNames().Contains("First") && !engine.GetNodeNames().Contains("Entry"),
            "neither use visual proxy nor module template is a runtime node");
        Check(!engine.GetEventNames().Contains("Done"), "Output has no bare global event");
        Check(engine.ModuleInstances["First"].PosX == 10 && engine.ModuleInstances["First"].Color == "red"
            && engine.ModuleInstances["First"].FlowID == "first", "use metadata survives initialization");
        foreach (var name in new[] { "Entry", "Exit", "First", "Second", "success", "error", "failed" })
            Check(typeof(InlineModules).GetProperty(name) == null, "no template, proxy or Branch-LHS accessor: " + name);
        var publisher = engine.GetNode("First.Exit");
        publisher.UpdateEventDescriptions();
        Check(publisher.EventDescriptions.Any(d => d.Index == 1 && d.Description == "First.Done")
            && publisher.EventDescriptions.Any(d => d.Index == 2 && d.Description == "First.Done"),
            "external Output connections do not rewrite Branch events");
        Check(publisher.GetFSMTransitions().Count(t => t.Trigger == engine.GetEvent("First.Done")) == 1,
            "duplicate Branch publishers connect once per node");
    }

    private static void RelativeAdditionalFiles(IFSMNodeFactory factory)
    {
        var flow = RelativeImports.Create(factory);
        var engine = flow.Engine;
        AssertRealAccessors(flow, engine);
        Check(RelativeImports.ModuleScripts.Length == 2, "relative transitive imports embedded once");
        Same(((IFSMNode)flow.Start).TargetState(flow.Launch), engine.GetNode("Check.LocalEntry"),
            "import uses the .fsm next to the importing script, not another basename match");
        Same(engine.GetNode("Check.LocalEntry").TargetState(engine.GetEvent("Check.Done")), engine.GetNode("Helper.Local"),
            "transitive dependency resolves relative to importing .fsm");
        Same(engine.GetNode("Helper.Local").TargetState(engine.GetEvent("Helper.Done")), flow.Finish,
            "transitively embedded module is usable without runtime files");
        Check(!engine.GetNodeNames().Contains("Check.Entry"), "unrelated AdditionalFile is not embedded by basename");
    }

    private static void EmbeddedModulesAndNamespace(IFSMNodeFactory factory)
    {
        // No script files are copied to the output; initialization must use embedded AdditionalFiles.
        var flow = EmbeddedModules.Create(factory);
        var engine = flow.Engine;
        AssertRealAccessors(flow, engine);
        Check(EmbeddedModules.ModuleScripts.Length == 2, "transitive .fsm imports are embedded");
        Same(((IFSMNode)flow.Area_Start).TargetState(flow.Area_Launch), engine.GetNode("Area.One.Retry"),
            "multi-Input module selects public Retry");
        foreach (var publisher in new[] { "Area.One.Retry", "Area.One.Private" })
            Same(engine.GetNode(publisher).TargetState(engine.GetEvent("Area.One.Done")), engine.GetNode("Area.Two.Entry"),
                "all Output publishers connect to selected public Input");
        Same(engine.GetNode("Area.Two.Private").TargetState(engine.GetEvent("Area.Two.Done")), flow.Area_Finish,
            "Namespace Output connects ordinary target");
        Same(engine.GetNode("Remote.Private").TargetState(engine.GetEvent("Remote.Done")), flow.Area_Finish,
            "fully qualified Output is not incorrectly prefixed by current Namespace");
        Same(((IFSMNode)flow.Area_One_Observer).TargetState(flow.Area_One_Custom), flow.Area_Finish,
            "ordinary dotted event and dotted node sharing an instance prefix stay ordinary");
        Same(flow.Area_One_Unused, engine.GetEvent("Area.One.Unused"), "ordinary dotted explicit event");
        Check(!ReferenceEquals(engine.GetEvent("Area.One.Done"), engine.GetEvent("Area.Two.Done")),
            "embedded instances have isolated events");
        Check(engine.ModuleInstances["Area.One"].PosY == 40 && engine.ModuleInstances["Area.One"].Color == "blue",
            "Namespace use metadata");
        foreach (var name in new[] { "Entry", "Retry", "Private", "One", "Area.One", "Area.Two", "Remote" })
            Check(!engine.GetNodeNames().Contains(name), "templates and visual proxies are not runtime nodes: " + name);
        Check(!engine.GetEventNames().Contains("Done") && !engine.GetEventNames().Contains("Area.Done"),
            "embedded Outputs only have instance-qualified identity");
        Check(typeof(EmbeddedModules).GetProperty("Area_Remote_Done") == null,
            "no accessor invents a locally qualified cross-Namespace Output");
    }

    private static void PlainScriptScopes(IFSMNodeFactory factory)
    {
        var flow = new PlainScopes(factory);
        var engine = flow.Engine;
        AssertRealAccessors(flow, engine);
        TestStartNode start = flow.Area_Start;
        TestEndNode end = flow.Area_End;
        TestIdleNode dotted = flow.Area_Dotted_Node;
        Same(start, engine.GetNode("Area.Start"), "Namespace node identity and typed 'as state' def");
        Same(flow.Area_Signal_Dot, engine.GetEvent("Area.Signal.Dot"), "ordinary dotted event identity");
        Same(((IFSMNode)start).TargetState(flow.Area_Signal_Dot), end, "ordinary dotted event is not node member access");
        Same(((IFSMNode)end).TargetState(flow.Area_TransitionOnly), dotted, "transition-only event accessor");
        Same(flow.Outside, engine.GetNode("Outside"), "Namespace scope ends at closing brace");
        Same(flow.Area_Declared, engine.GetEvent("Area.Declared"), "explicit as event");
        Same(flow.Area_Bare, engine.GetEvent("Area.Bare"), "explicit event");
        foreach (var name in new[] { "Waiting", "Failure", "ErrorEvent", "BreakEvent", "CancelEvent", "Custom", "Negative" })
        {
            var accessor = typeof(PlainScopes).GetProperty("Area_" + name);
            Check(accessor != null, "all ordinary Branch forms have event accessors: " + name);
            Same(accessor!.GetValue(flow)!, engine.GetEvent("Area." + name), "Branch RHS is event: " + name);
        }
        Check(typeof(PlainScopes).GetProperty("Ghost") == null && typeof(PlainScopes).GetProperty("Phantom") == null
            && typeof(PlainScopes).GetProperty("Commented") == null, "comments and CODESTRING do not declare objects");
        Check(PlainScopes.ModuleScripts.Length == 0, "fake imports in comments and strings are ignored");
    }

    private static void ExistingPlainScript(IFSMNodeFactory factory)
    {
        var flow = ATest.Create(factory);
        AssertRealAccessors(flow, flow.Engine);
        Check(flow.Engine.GetNodeNames().Count() == 15, "existing plain fixture keeps all nodes");
        Same(((IFSMNode)flow.Start).TargetState(flow.NextEvent), flow.Check, "existing plain transition");
        Same(((IFSMNode)flow.Check).TargetState(flow.BreakEvent), flow.End, "existing Branch transition");
    }

    // Exercise every public generated accessor, not just the ones named by the test.
    private static void AssertRealAccessors(object flow, FSMEngine engine)
    {
        foreach (var property in flow.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (typeof(IFSMNode).IsAssignableFrom(property.PropertyType))
            {
                var node = property.GetValue(flow) as IFSMNode;
                Check(node != null, $"{property.Name} must return a real runtime node");
                Same(node!, engine.GetNode(node!.Name), property.Name);
            }
            else if (property.PropertyType == typeof(FSMEvent))
            {
                var ev = property.GetValue(flow) as FSMEvent;
                Check(ev != null, $"{property.Name} must return a real runtime event");
                Same(ev!, engine.GetEvent(ev!.EventName), property.Name);
            }
        }
    }

    private sealed class ReflectionNodeFactory : IFSMNodeFactory
    {
        private readonly System.Collections.Generic.Dictionary<string, Type> types;

        public ReflectionNodeFactory(Assembly[] assemblies)
        {
            types = assemblies.SelectMany(a => a.GetTypes())
                .Where(t => !t.IsAbstract && typeof(IFSMNode).IsAssignableFrom(t))
                .Select(t => (Type: t, Attribute: t.GetCustomAttribute<FSMNodeAttribute>()))
                .Where(x => x.Attribute != null)
                .GroupBy(x => x.Attribute!.Key)
                .ToDictionary(g => g.Key, g => g.First().Type);
        }

        public IFSMNode CreateNode(string name) => (IFSMNode)Activator.CreateInstance(GetNodeType(name))!;
        public Type GetNodeType(string name) => types[name];
        public string GetNodeFeatureName(Type type) => type.GetCustomAttribute<FSMNodeAttribute>()!.Key;
    }

    private static void Same(object actual, object expected, string message) =>
        Check(ReferenceEquals(actual, expected), message);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
