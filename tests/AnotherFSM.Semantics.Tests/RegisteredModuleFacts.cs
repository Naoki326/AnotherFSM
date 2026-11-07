namespace AnotherFSM.Semantics.Tests;

public sealed class RegisteredModuleFacts
{
    [Fact]
    public void Transitive_imports_retain_the_root_configuration_and_actual_importing_source()
    {
        var root = FsmAnalysis.Analyze(new("/host/main.fsm", 1, "use Registered as Work;"));
        var registered = FsmAnalysis.Analyze(new("/mapped/actual.fsm", 1,
            "import Support; module Registered { input Entry; def Entry(Key) {} }"));
        var support = FsmAnalysis.Analyze(new("/another/support.fsm", 1, "module Support { input S; def S(Key) {} }"));
        var requests = new List<ModuleImport>();
        var graph = SemanticGraph.Build(new[] { root }, request => {
            requests.Add(request);
            return request.RootDocumentId != root.Document.Id ? null : request.Name switch {
                "Registered" => registered, "Support" => support, _ => null
            };
        }, registeredModules: _ => new[] { "Registered", "Support" });
        Assert.Equal("resolved", graph.Navigate(root.Document.Id, 4, "module").Status);
        Assert.Contains(new ModuleImport("/host/main.fsm", "/mapped/actual.fsm", "Support"), requests);
    }

    [Fact]
    public void Host_registered_module_file_is_available_before_use_without_a_synthetic_import_location()
    {
        var root = FsmAnalysis.Analyze(new("/host/main.fsm", 1, "use Registered as Work;"));
        var module = FsmAnalysis.Analyze(new("/config/modules/actual.fsm", 1,
            "module Registered { input Entry; def Entry(Key) {} }"));
        var graph = SemanticGraph.Build(new[] { root }, (from, name) => from == "/host/main.fsm" && name == "Registered" ? module : null,
            registeredModules: path => path == "/host/main.fsm" ? new[] { "Registered" } : Array.Empty<string>());
        var query = graph.Navigate("/host/main.fsm", "use ".Length, "module");
        Assert.Equal("resolved", query.Status);
        Assert.Equal("/config/modules/actual.fsm", query.Locations.Single().DocumentId);
        Assert.Equal("Registered", module.Document.Text.Substring(query.Locations.Single().Span.Start, query.Locations.Single().Span.Length));
    }
}
