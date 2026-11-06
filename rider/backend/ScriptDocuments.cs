using AnotherFSM.Semantics;
using JetBrains.Application.Parts;
using JetBrains.DocumentManagers;
using JetBrains.DocumentModel;
using JetBrains.Application.Changes;
using JetBrains.ReSharper.Psi.impl;
using JetBrains.Lifetimes;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.Util;

namespace AnotherFSM.Rider;

/// <summary>Project membership comes only from Rider's project model. Imports follow one exact physical path.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class ScriptDocuments
{
    private readonly ISolution solution;
    private readonly IPsiServices psi;
    private readonly DocumentManager documentManager;
    private readonly PsiProjectFileTypeCoordinator fileTypes;
    private readonly ScriptIndex index;
    private readonly Dictionary<string, IPsiSourceFile[]> inventory = new();
    private readonly Dictionary<string, IPsiSourceFile> dependencies = new(StringComparer.OrdinalIgnoreCase);
    private sealed record GraphEntry(SemanticGraph Graph, IReadOnlyDictionary<string, IPsiSourceFile> Sources);
    private readonly Dictionary<string, GraphEntry> graphs = new();
    private readonly object gate = new();
    private long structure = -1;
    private bool closed;

    public ScriptDocuments(Lifetime lifetime, ISolution solution, IPsiServices psi, DocumentManager documentManager,
        PsiProjectFileTypeCoordinator fileTypes, ScriptIndex index)
    {
        this.solution = solution;
        this.psi = psi;
        this.documentManager = documentManager;
        this.fileTypes = fileTypes;
        this.index = index;
        void InvalidateGraphs() { lock (gate) graphs.Clear(); }
        index.Invalidated += InvalidateGraphs;
        lifetime.OnTermination(() => {
            index.Invalidated -= InvalidateGraphs;
            lock (gate) { closed = true; inventory.Clear(); dependencies.Clear(); graphs.Clear(); }
        });
    }

    public static string Id(IPsiSourceFile file) => file.GetLocation().FullPath.Replace('\\', '/');
    public static bool IsScriptName(string name) => name.EndsWith(".fsm", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".FSMScript", StringComparison.OrdinalIgnoreCase);
    public IPsiModule[] Modules() => psi.Modules.GetSourceModules().Where(module => module.IsValid())
        .Select(module => module.ContainingProjectModule).OfType<IProject>().Distinct()
        .Where(project => !ReferenceEquals(project, solution.MiscFilesProject) && !ReferenceEquals(project, solution.SolutionProject))
        // Content/additional-file language views are not independent consuming C# hosts.
        .SelectMany(project => project.TargetFrameworkIds.Select(tfm => psi.Modules.GetPrimaryPsiModule(project, tfm)))
        .Where(module => module != null && module.IsValid()).DistinctBy(module => module.GetPersistentID()).ToArray();

    public IPsiSourceFile[] Roots(IPsiModule module)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (structure != index.StructureVersion)
            {
                inventory.Clear(); dependencies.Clear(); structure = index.StructureVersion;
            }
            if (inventory.TryGetValue(module.GetPersistentID(), out var cached)) return cached;
            if (module.ContainingProjectModule is not IProject project) return Array.Empty<IPsiSourceFile>();
            var files = new List<IPsiSourceFile>();
            foreach (var item in project.GetAllProjectFiles(file => IsScriptName(file.Name)))
            {
                JetBrains.Application.Interruption.Current.CheckAndThrow();
                if (!item.IsValid() || !item.Location.ExistsFile) continue;
                var sources = psi.Modules.GetPsiSourceFilesFor(item).ToArray().Where(file => ScriptIndex.IsScript(file)
                    && file.PsiModule.GetPersistentID() == module.GetPersistentID()).ToArray();
                // None/Content/physical EmbeddedResource can be project items without a language PSI view.
                // This text-only view supplies a range to native Find Usages; it does not add project membership,
                // create a code model, build the project or register a second analysis workspace.
                var source = sources.FirstOrDefault();
                if (source != null)
                {
                    if (sources.Any(candidate => !ReferenceEquals(candidate.Document, source.Document)))
                        throw new OperationCanceledException("Ambiguous host document views");
                }
                else source = TextView(item.Location, module, item);
                if (!files.Any(existing => StringComparer.OrdinalIgnoreCase.Equals(Id(existing), Id(source)))) files.Add(source);
            }
            if (inventory.Count >= 128) inventory.Clear();
            return inventory[module.GetPersistentID()] = files.ToArray();
        }
    }

    public IPsiSourceFile[] Find(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) return Array.Empty<IPsiSourceFile>();
        return Modules().SelectMany(Roots).Where(file => StringComparer.OrdinalIgnoreCase.Equals(Id(file), path.Replace('\\', '/'))).ToArray();
    }

    public IPsiSourceFile? Import(IPsiModule module, string importingPath, string name)
    {
        var path = ResolveImportPath(importingPath, name);
        if (path == null) return null;
        // A linked file's Location is physical. No virtual project folder participates in resolution.
        return Roots(module).FirstOrDefault(file => file.GetLocation().Equals(path)) ?? TextView(path, module);
    }

    private static VirtualFileSystemPath? ResolveImportPath(string importingPath, string name)
    {
        // Names are supplied by the authoritative grammar; no new path syntax or cwd fallback.
        if (!Path.IsPathFullyQualified(importingPath) || name.IndexOfAny(['/', '\\', ':', '\0']) >= 0) return null;
        var directory = Path.GetDirectoryName(importingPath);
        if (directory == null) return null;
        var path = VirtualFileSystemPath.Parse(Path.Combine(directory, name + ".fsm"), InteractionContext.SolutionContext);
        return path.ExistsFile ? path : null;
    }

    private IPsiSourceFile TextView(VirtualFileSystemPath path, IPsiModule module, IProjectFile? projectFile = null)
    {
        var key = module.GetPersistentID() + "|" + path.FullPath;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (dependencies.TryGetValue(key, out var cached) && cached.IsValid()) return cached;
            if (dependencies.Count >= 2048) dependencies.Clear();
            bool Valid(PsiSourceFileFromPath _) => !closed && module.IsValid() && path.ExistsFile
                && (projectFile == null || projectFile.IsValid());
            return dependencies[key] = projectFile == null
                ? new PsiSourceFileFromPath(solution.ProjectFileExtensions, fileTypes, module, path, Valid, _ => TextProperties.Instance,
                    documentManager, UniversalModuleReferenceContext.Instance)
                : new ProjectTextView(solution.ProjectFileExtensions, fileTypes, module, projectFile, Valid, documentManager);
        }
    }

    public SemanticGraph Graph(IPsiModule module, long epoch, out IReadOnlyDictionary<string, IPsiSourceFile> sources)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (epoch != index.Epoch) throw new OperationCanceledException("AnotherFSM graph context expired");
            if (graphs.TryGetValue(module.GetPersistentID(), out var cached)) { sources = cached.Sources; return cached.Graph; }
        }
        var files = new Dictionary<string, IPsiSourceFile>(StringComparer.OrdinalIgnoreCase);
        DocumentAnalysis Read(IPsiSourceFile file)
        {
            files[Id(file)] = file;
            return index.Read(file, epoch);
        }
        var graph = SemanticGraph.Build(Roots(module).Select(Read).ToArray(), (from, name) => {
            var file = Import(module, from, name);
            return file == null ? null : Read(file);
        }, checkHostCancellation: () => {
            JetBrains.Application.Interruption.Current.CheckAndThrow();
            if (epoch != index.Epoch) throw new OperationCanceledException("AnotherFSM graph context expired");
        });
        if (epoch != index.Epoch) throw new OperationCanceledException("AnotherFSM graph context expired");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (epoch != index.Epoch) throw new OperationCanceledException("AnotherFSM graph context expired");
            if (graphs.Count >= 64) graphs.Clear();
            graphs[module.GetPersistentID()] = new(graph, files);
        }
        sources = files;
        return graph;
    }

    public string? CurrentText(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) return null;
        var files = Find(path);
        if (files.Length > 1) return null;
        if (files.Length == 1) return files[0].Document.GetText();
        var target = VirtualFileSystemPath.Parse(path, InteractionContext.SolutionContext);
        return target.ExistsFile ? documentManager.GetOrCreateDocument(target).GetText() : null;
    }

    public SemanticGraph Local(string path, long version, string text, long epoch)
    {
        void Check()
        {
            JetBrains.Application.Interruption.Current.CheckAndThrow();
            if (index.Epoch != epoch) throw new OperationCanceledException("Local query expired");
        }
        var root = FsmAnalysis.Analyze(new(path, version, text), checkHostCancellation: Check);
        return SemanticGraph.Build(new[] { root }, (from, name) => {
            Check();
            var target = ResolveImportPath(from, name);
            return target != null ? index.Read(target.FullPath.Replace('\\', '/'), documentManager.GetOrCreateDocument(target), epoch) : null;
        }, checkHostCancellation: Check);
    }

    /// <summary>Rider binds ordinary project editors by IProjectFile, not by the path-only document table.</summary>
    private sealed class ProjectTextView : PsiSourceFileFromPath, IPsiSourceFile, IPsiSourceFileImpl
    {
        private readonly IProjectFile file;
        private readonly DocumentManager manager;
        public ProjectTextView(IProjectFileExtensions extensions, PsiProjectFileTypeCoordinator coordinator, IPsiModule module,
            IProjectFile file, Func<PsiSourceFileFromPath, bool> valid, DocumentManager manager)
            : base(extensions, coordinator, module, file.Location, valid, _ => TextProperties.Instance,
                manager, UniversalModuleReferenceContext.Instance)
        { this.file = file; this.manager = manager; }
        public new IDocument Document => manager.GetOrCreateDocument(file);
        public new ModificationStamp? InMemoryModificationStamp => Document.LastModificationStamp;
        IDocument? IPsiSourceFileImpl.TryGetDocumentIfAllocated() => manager.TryGetDocument(file);
    }

    private sealed class TextProperties : IPsiSourceFileProperties
    {
        public static readonly TextProperties Instance = new();
        public bool ShouldBuildPsi => false;
        public bool IsGeneratedFile => false;
        public bool IsICacheParticipant => false;
        public bool ProvidesCodeModel => false;
        public bool IsNonUserFile => false;
        public IEnumerable<string> GetPreImportedNamespaces() => Array.Empty<string>();
        public string GetDefaultNamespace() => "";
        public ICollection<PreProcessingDirective> GetDefines() => Array.Empty<PreProcessingDirective>();
    }
}
