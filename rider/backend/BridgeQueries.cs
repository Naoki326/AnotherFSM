using System.Text.Json;
using AnotherFSM.Semantics;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;

namespace AnotherFSM.Rider;

/// <summary>Versioned RD payloads. All PSI work is scheduled by the host, never waited on by the UI.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class BridgeQueries(ISolution solution, IPsiServices psi, ScriptIndex index)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private sealed record Request(string Action, string Path, string Text, long DocumentVersion, int Offset,
        string RequestId, bool Implementation = false, string? Session = null, long ContextVersion = -1, TypeIdentity? Target = null);
    private sealed record Reply(string Status, string Reason, string RequestId, long DocumentVersion, string Session,
        long ContextVersion, SourceSpan? Span = null, TypeIdentity? Target = null);

    public async Task<string> Query(Lifetime lifetime, string json)
    {
        Request? request;
        try { request = JsonSerializer.Deserialize<Request>(json, Json); }
        catch (JsonException) { return "{\"status\":\"invalidRequest\"}"; }
        if (request == null || request.Text == null || request.Path == null || request.RequestId == null
            || request.Offset < 0 || request.Offset > request.Text.Length) return "{\"status\":\"invalidRequest\"}";
        Reply reply;
        if (request.Action == "navigateType")
        {
            var validation = await psi.Files.CommitWithRetryBackgroundRead(lifetime, () => Resolve(request with { Action = "resolve" })).ConfigureAwait(false);
            reply = validation.Status == "type" && validation.Target == request.Target
                ? await psi.Files.CommitWithRetryMainRead(lifetime, () => Navigate(request)).ConfigureAwait(false)
                : Response(request, "stale", "Binding changed; run navigation again");
        }
        else reply = await psi.Files.CommitWithRetryBackgroundRead(lifetime, () => Resolve(request)).ConfigureAwait(false);
        return JsonSerializer.Serialize(reply, Json);
    }
    private Reply Response(Request request, string status, string reason, SourceSpan? span = null, TypeIdentity? target = null)
        => new(status, reason, request.RequestId, request.DocumentVersion, index.Session.ToString(), index.Epoch, span, target);

    private IPsiSourceFile[] FindFiles(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) return Array.Empty<IPsiSourceFile>();
        var location = VirtualFileSystemPath.Parse(path, InteractionContext.SolutionContext);
        var candidates = solution.FindProjectItemsByLocation(location).OfType<IProjectFile>()
            .SelectMany(file => psi.Modules.GetPsiSourceFilesFor(file).ToArray()).Where(ScriptIndex.IsScript)
            .Where(file => file.PsiModule.ContainingProjectModule is IProject project
                && !ReferenceEquals(project, solution.MiscFilesProject) && !ReferenceEquals(project, solution.SolutionProject));
        var unique = new List<IPsiSourceFile>();
        foreach (var file in candidates)
        {
            // The host can expose the same project document through multiple PSI views.
            // Merge only identical document objects in the exact same module/TFM, never distinct hosts.
            if (!unique.Any(existing => existing.PsiModule.GetPersistentID() == file.PsiModule.GetPersistentID()
                && existing.PsiModule.TargetFrameworkId.UniqueString == file.PsiModule.TargetFrameworkId.UniqueString
                && ReferenceEquals(existing.Document, file.Document))) unique.Add(file);
        }
        return unique.ToArray();
    }
    private Reply Resolve(Request request)
    {
        if (request.Action != "resolve") return Response(request, "unsupported", "Unknown operation");
        var epoch = index.Epoch;
        var files = FindFiles(request.Path);
        if (files.Length > 1) return Response(request, "unresolved", "Multiple document/host contexts; not supported by this development slice: "
            + string.Join(", ", files.Select(file => file.PsiModule.GetPersistentID() + "/" + file.PsiModule.TargetFrameworkId.UniqueString)));
        var file = files.SingleOrDefault();
        var hasHost = file != null && !ReferenceEquals(file.PsiModule.ContainingProjectModule, solution.MiscFilesProject);
        if (hasHost && file!.Document.GetText() != request.Text)
            return Response(request, "analyzing", "Waiting for the host's current unsaved buffer; retry");
        var analysis = hasHost ? index.Read(file!) : FsmAnalysis.Analyze(new(request.Path, request.DocumentVersion, request.Text),
            checkHostCancellation: () => JetBrains.Application.Interruption.Current.CheckAndThrow());
        var factory = analysis.FindFactoryDeclaration(request.Offset);
        var keyPosition = factory != null && (!factory.ImplicitKey || request.Implementation);
        var node = keyPosition ? factory : analysis.FindNodeDeclaration(request.Offset);
        if (node == null) return Response(request, "unresolved", "No reliable ordinary Node at this position; other DSL names are not supported in this slice");
        if (!request.Implementation && !keyPosition) return Response(request, "dsl", "Node declaration", node.NameSpan);
        if (!hasHost) return Response(request, "unresolved", "C# host context unavailable; local DSL navigation remains available");
        var binding = NodeBindings.Resolve(node, index.Catalog(file!.PsiModule));
        if (epoch != index.Epoch) return Response(request, "stale", "Context changed; retry");
        return binding.State == BindingState.Determined
            ? Response(request, "type", binding.Reason, target: binding.Candidates.Single())
            : Response(request, "unresolved", binding.State + ": " + binding.Reason);
    }
    private Reply Navigate(Request request)
    {
        if (request.Session != index.Session.ToString() || request.ContextVersion != index.Epoch || request.Target == null)
            return Response(request, "stale", "Result expired; run navigation again");
        var files = FindFiles(request.Path);
        if (files.Length != 1 || files[0].Document.GetText() != request.Text)
            return Response(request, "stale", "Document changed; run navigation again");
        var target = TypeCatalog.Resolve(psi, request.Target);
        if (target == null || !target.GetSourceFiles().Any()) return Response(request, "unresolved", "Current-project source unavailable");
        target.Navigate(true);
        // A command dispatch, not a fabricated claim that the IDE selected the correct source.
        return Response(request, "navigationDispatched", "Navigation delegated to Rider");
    }
}
