using AnotherFSM.Semantics;
using JetBrains.Application.Parts;
using JetBrains.DocumentManagers;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.Util;

namespace AnotherFSM.Rider;

/// <summary>Reads an optional, adjacent configuration as text. Never builds, restores, loads DLLs or invokes business code.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class BindingSettings(DocumentManager documents)
{
    public static string? ProjectPath(IPsiModule module) => module.ContainingProjectModule is IProject project
        ? project.ProjectFileLocation.FullPath.Replace('\\', '/') : null;

    public BindingOverride Read(IPsiModule module, string script)
    {
        var projectPath = ProjectPath(module);
        if (projectPath == null) return new(KeyComparison.OrdinalIgnoreCase, Array.Empty<ConfiguredMapping>(), Array.Empty<ModuleFile>());
        var path = VirtualFileSystemPath.Parse(Path.Combine(Path.GetDirectoryName(projectPath)!, "anotherfsm.json"), InteractionContext.SolutionContext);
        if (!path.ExistsFile) return new(KeyComparison.OrdinalIgnoreCase, Array.Empty<ConfiguredMapping>(), Array.Empty<ModuleFile>());
        // Prefer a host project document when it is a real project item; path-only buffers need not be the editor buffer.
        var project = (IProject)module.ContainingProjectModule;
        var items = project.GetAllProjectFiles(file => file.Location.Equals(path)).ToArray();
        var text = items.Length == 1 ? documents.GetOrCreateDocument(items[0]).GetText() : documents.GetOrCreateDocument(path).GetText();
        return BindingConfiguration.Parse(path.FullPath, text).Select(projectPath, Framework(module), script);
    }

    public static string Framework(IPsiModule module)
    {
        var identity = module.TargetFrameworkId.UniqueString;
        // Configuration accepts the supported SDK monikers; identity itself always retains the host's full TFM ID.
        if (identity.Contains(".NETCoreApp,Version=v8.0", StringComparison.OrdinalIgnoreCase)) return "net8.0";
        if (identity.Contains(".NETCoreApp,Version=v10.0", StringComparison.OrdinalIgnoreCase)) return "net10.0";
        if (identity.Contains(".NETStandard,Version=v2.0", StringComparison.OrdinalIgnoreCase)) return "netstandard2.0";
        return identity;
    }
}
