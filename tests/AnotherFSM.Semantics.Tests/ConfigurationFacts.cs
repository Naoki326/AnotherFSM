namespace AnotherFSM.Semantics.Tests;

public sealed class ConfigurationFacts
{
    [Fact]
    public void Overrides_apply_only_to_the_selected_host_framework_and_script_with_config_relative_paths()
    {
        var directory = Path.GetFullPath("configuration-fixture");
        var path = Path.Combine(directory, "anotherfsm.json");
        var configuration = BindingConfiguration.Parse(path, """
            {"version":1,"contexts":[{
              "host":"App/App.csproj", "targetFramework":"net8.0", "scripts":["Shared/flow.fsm"],
              "keyComparison":"Ordinal",
              "mappings":[{"key":"Special","type":{"project":"Library/Library.csproj","clrName":"Nodes.Container+Worker"}}],
              "modules":[{"name":"Registered","file":"Modules/actual.fsm"}]
            }]}
            """);
        var selected = configuration.Select(Path.Combine(directory, "App/App.csproj"), "net8.0", Path.Combine(directory, "Shared/flow.fsm"));

        Assert.Null(selected.Error);
        Assert.Equal(KeyComparison.Ordinal, selected.Comparison);
        Assert.Equal(Path.Combine(directory, "Library/Library.csproj").Replace('\\', '/'), selected.Mappings.Single().Type.Project);
        Assert.Equal(Path.Combine(directory, "Modules/actual.fsm").Replace('\\', '/'), selected.Modules.Single().File);
        Assert.Empty(configuration.Select(Path.Combine(directory, "Other.csproj"), "net8.0", Path.Combine(directory, "Shared/flow.fsm")).Mappings);
        Assert.Empty(configuration.Select(Path.Combine(directory, "App/App.csproj"), "net10.0", Path.Combine(directory, "Shared/flow.fsm")).Mappings);
        Assert.Empty(configuration.Select(Path.Combine(directory, "App/App.csproj"), "net8.0", Path.Combine(directory, "Other.fsm")).Mappings);
    }
}
