using System.Text.Json;
using AnotherFSM.Semantics;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Modules;

namespace AnotherFSM.Rider;

/// <summary>Versioned RD payloads. All PSI work is scheduled by the host, never waited on by the UI.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class BridgeQueries(IPsiServices psi, ScriptIndex index, ScriptDocuments scripts, ResultLifecycle results, BindingSettings settings)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private sealed record Request(string Action, string Path, string Text, long DocumentVersion, int Offset,
        string RequestId, bool Implementation = false, string? Session = null, long ContextVersion = -1, TypeIdentity? Target = null,
        string? ListId = null, string Operation = "declaration", string QueryAction = "resolve", Location? Selected = null, string? Host = null);
    private sealed record Location(string DocumentId, SourceSpan Span, string Kind, string Label, IReadOnlyList<string> Instances, string Text,
        IReadOnlyList<string>? Hosts = null);
    private sealed record HostChoice(string Id, string Label);
    private sealed record HostGraph(IPsiModule Module, IPsiSourceFile Source, SemanticGraph Graph);
    private sealed record Reply(string Status, string Reason, string RequestId, long DocumentVersion, string Session,
        long ContextVersion, SourceSpan? Span = null, TypeIdentity? Target = null, ReferenceQueryStatus? Reference = null,
        IReadOnlyList<Location>? Locations = null, IReadOnlyList<HostChoice>? Hosts = null,
        ExternalTypeNavigation.Result? ExternalPresentation = null);

    public async Task<string> Query(Lifetime lifetime, string json)
    {
        Request? request;
        try { request = JsonSerializer.Deserialize<Request>(json, Json); }
        catch (JsonException) { return "{\"status\":\"invalidRequest\"}"; }
        if (request == null || request.Text == null || request.Path == null || request.RequestId == null
            || request.Offset < 0 || request.Offset > request.Text.Length) return "{\"status\":\"invalidRequest\"}";
        Reply reply;
        if (request.Action == "referenceStatus")
        {
            reply = await psi.Files.CommitWithRetryMainRead(lifetime, () => {
                var status = request.ListId == null ? null : results.GetStatus(request.ListId);
                return status == null
                    ? Response(request, "unknownQuery", "Native query identity is unavailable or ambiguous")
                    : Response(request, "referenceStatus", "Native query status") with { Reference = status };
            }).ConfigureAwait(false);
        }
        else if (request.Action == "navigateType")
        {
            var validation = await psi.Files.CommitWithRetryBackgroundRead(lifetime, () => Resolve(request with { Action = "resolve" })).ConfigureAwait(false);
            reply = validation.Status == "type" && validation.Target == request.Target
                ? await psi.Files.CommitWithRetryMainRead(lifetime, () => Navigate(request, lifetime, validation.Reason)).ConfigureAwait(false)
                : Response(request, "stale", "Binding changed; run navigation again");
        }
        else reply = await psi.Files.CommitWithRetryBackgroundRead(lifetime, () => {
            if (request.Action != "validateLocation") return Resolve(request);
            if (request.Session != index.Session.ToString() || request.ContextVersion != index.Epoch || request.Selected == null)
                return Response(request, "stale", "Result expired; refresh before navigating");
            var current = Resolve(request with { Action = request.QueryAction });
            var selected = current.Locations?.SingleOrDefault(location => SameLocation(location, request.Selected));
            return selected == null || current.ContextVersion != request.ContextVersion
                ? Response(request, "stale", "The exact source identity is no longer current; refresh")
                : current with { Status = "dsl", Span = selected.Span, Locations = new[] { selected } };
        }).ConfigureAwait(false);
        return JsonSerializer.Serialize(reply, Json);
    }
    private static bool SameLocation(Location left, Location right) =>
        StringComparer.OrdinalIgnoreCase.Equals(left.DocumentId, right.DocumentId) && left.Span == right.Span
        && left.Kind == right.Kind && left.Label == right.Label && left.Text == right.Text
        && left.Instances.SequenceEqual(right.Instances) && (left.Hosts ?? Array.Empty<string>()).SequenceEqual(right.Hosts ?? Array.Empty<string>());
    private Reply Response(Request request, string status, string reason, SourceSpan? span = null, TypeIdentity? target = null)
        => new(status, reason, request.RequestId, request.DocumentVersion, index.Session.ToString(), index.Epoch, span, target);

    private Reply Resolve(Request request)
    {
        if (request.Action is not ("resolve" or "references" or "templateReferences")) return Response(request, "unsupported", "Unknown operation");
        var epoch = index.Epoch;
        if (request.Host != null && (request.Session != index.Session.ToString() || request.ContextVersion != epoch))
            return Response(request, "stale", "Selected host context expired; run navigation again");
        var contexts = new List<HostGraph>();
        if (Path.IsPathFullyQualified(request.Path))
            foreach (var module in scripts.Modules())
            {
                var candidate = scripts.Graph(module, epoch, out var sources);
                if (sources.TryGetValue(request.Path, out var source)) contexts.Add(new(module, source, candidate));
            }
        if (contexts.Any(context => context.Source.Document.GetText() != request.Text))
            return Response(request, "analyzing", "Waiting for the host's current shared/unsaved buffer; retry");
        var selected = request.Host == null ? null : contexts.SingleOrDefault(context => context.Module.GetPersistentID() == request.Host);
        if (request.Host != null && selected == null) return Response(request, "stale", "Selected consuming host is no longer available");
        // DSL source identity is independent of host selection. C# operations require an explicit choice below.
        var current = selected ?? (contexts.Count == 1 ? contexts[0] : null);
        var file = current?.Source;
        var graph = current?.Graph ?? (contexts.Count > 0 ? contexts[0].Graph : scripts.Local(request.Path, request.DocumentVersion, request.Text, epoch));
        var document = graph.Documents.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Document.Id, request.Path));
        if (document == null || document.Document.Text != request.Text)
            return Response(request, "analyzing", "Waiting for the current shared document buffer");
        var id = document.Document.Id;
        var factoryHere = document.FindFactoryDeclaration(request.Offset);
        var needsHost = request.Implementation || request.Operation == "branchMetadata" || factoryHere is { ImplicitKey: false };
        if (request.Action == "resolve" && needsHost && request.Host == null && contexts.Count > 1)
        {
            var choices = contexts.Select(context => {
                var source = context.Graph.FindNodeSource(id, request.Offset);
                var binding = source == null ? null : Bind(source.Node, context.Module, source.DocumentId);
                return new HostChoice(context.Module.GetPersistentID(), BindingSettings.HostLabel(context.Module) + " — "
                    + (binding == null ? "No intrinsic C# binding" : binding.State + ": " + binding.Reason)
                    + (binding?.Candidates.Count > 0 ? " · " + string.Join(", ", binding.Candidates.Select(target => target.Module + ":" + target.ClrName)) : ""));
            }).ToArray();
            if (epoch != index.Epoch) return Response(request, "stale", "Host contexts changed; retry");
            return Response(request, "hosts", "Choose the consuming project / TFM; selection only filters this operation") with { Hosts = choices };
        }
        if (request.Action != "resolve")
        {
            // Aggregate exact source identities across solution scripts, not global same-name strings.
            var queries = new List<(SemanticGraph Graph, SemanticQuery Query, string Host)> {
                (graph, graph.FindReferences(id, request.Offset, request.Action == "templateReferences"),
                    contexts.FirstOrDefault(context => ReferenceEquals(context.Graph, graph)) is { } owner ? BindingSettings.HostLabel(owner.Module) : "") };
            foreach (var context in contexts.Where(context => !ReferenceEquals(context.Graph, graph)))
                queries.Add((context.Graph, context.Graph.FindReferences(id, request.Offset, request.Action == "templateReferences"), BindingSettings.HostLabel(context.Module)));
            var reply = Locations(request, epoch, queries, references: true);
            return contexts.Count == 0 ? reply with { Reason = reply.Reason + "; local script results only: C# host/solution context unavailable" } : reply;
        }
        if (request.Operation == "branchMetadata")
        {
            var branch = graph.FindBranchSource(id, request.Offset);
            if (branch == null) return Response(request, "unresolved", "No reliable Node/Branch index at this position");
            var summary = $"Branch {branch.Index} of Node {branch.Owner.Node.Name}; actual DSL integer, not a C# enum or return/yield location";
            if (file == null) return Response(request, "metadata", summary + "; C# host context unavailable");
            var branchBinding = Bind(branch.Owner.Node, file.PsiModule, branch.Owner.DocumentId);
            if (branchBinding.State != BindingState.Determined) return Response(request, "metadata", summary + "; " + branchBinding.Reason);
            summary += "; " + branchBinding.Reason;
            var metadata = TypeCatalog.BranchMetadata(psi, branchBinding.Candidates.Single(), branch.Index);
            if (epoch != index.Epoch) return Response(request, "stale", "Branch context changed; retry");
            if (metadata == null) return Response(request, "metadata", summary + "; no explicit, source-locatable index metadata");
            var location = new Location(metadata.DocumentId, metadata.Span, "BranchMetadata", summary + "; explicit FSMNode.Indexes metadata (not runtime registration proof)",
                Array.Empty<string>(), metadata.Text);
            return Response(request, "dsl", location.Label, location.Span) with { Locations = new[] { location } };
        }
        var nodeSource = graph.FindNodeSource(id, request.Offset);
        var factory = document.FindFactoryDeclaration(request.Offset);
        var keyPosition = factory != null && (!factory.ImplicitKey || request.Implementation);
        if (request.Implementation || keyPosition)
        {
            // A lexical factory header alone cannot bypass graph refusals (legacy module forms,
            // private/invalid interfaces, collisions or an unavailable template).
            var node = nodeSource?.Node;
            if (node == null) return Response(request, "unresolved", "This DSL symbol has no intrinsic C# type target; Output/Event/Module/instance/Namespace are not nodes");
            if (file == null) return Response(request, "unresolved", "C# host context unavailable; local DSL navigation remains available");
            var binding = Bind(node, file.PsiModule, nodeSource!.DocumentId);
            if (epoch != index.Epoch) return Response(request, "stale", "Context changed; retry");
            return binding.State == BindingState.Determined
                ? Response(request, "type", binding.Reason, target: binding.Candidates.Single())
                : Response(request, "unresolved", binding.State + ": " + binding.Reason);
        }
        var navigation = contexts.Count == 0 ? new[] { (graph, graph.Navigate(id, request.Offset, request.Operation), "") }
            : contexts.Select(context => (context.Graph, context.Graph.Navigate(id, request.Offset, request.Operation), BindingSettings.HostLabel(context.Module))).ToArray();
        return Locations(request, epoch, navigation, references: false);
    }

    private NodeBinding Bind(NodeDeclaration node, IPsiModule module, string script)
        => NodeBindings.Resolve(node, index.Catalog(module), TypeCatalog.Options(module, settings.Read(module, script)));
    private Reply Locations(Request request, long epoch, IEnumerable<(SemanticGraph Graph, SemanticQuery Query, string Host)> queries, bool references)
    {
        var all = queries.ToArray();
        var locations = all.SelectMany(pair => pair.Query.Locations.Select(location => {
            var document = pair.Graph.Documents.Single(item => item.Document.Id == location.DocumentId).Document;
            return new Location(location.DocumentId, location.Span, location.Kind, location.Label, location.Instances, document.Text,
                pair.Host.Length == 0 ? Array.Empty<string>() : new[] { pair.Host });
        })).GroupBy(location => (DocumentId: location.DocumentId.ToUpperInvariant(), location.Span, location.Kind)).Select(group => group.First() with {
            Instances = group.SelectMany(location => location.Instances).Distinct().Order().ToArray(),
            Hosts = group.SelectMany(location => location.Hosts ?? Array.Empty<string>()).Distinct().Order().ToArray()
        }).OrderBy(location => location.DocumentId, StringComparer.OrdinalIgnoreCase).ThenBy(location => location.Span.Start).ToArray();
        if (epoch != index.Epoch) return Response(request, "stale", "Graph changed during the query; retry");
        var reason = string.Join("; ", all.Select(pair => pair.Query.Reason).Distinct());
        // A single introduction/use position still is not a declaration. Preserve the core's
        // explanatory list semantics instead of inventing certainty from row cardinality.
        var status = locations.Length > 0 ? (references || locations.Length > 1 || all.Any(pair => pair.Query.Status == "locations") ? "locations" : "dsl")
            : all.Any(pair => pair.Query.Status == "metadata") ? "metadata" : references && all.Any(pair => pair.Query.Status != "unresolved") ? "locations" : "unresolved";
        return Response(request, status, reason, locations.Length == 1 ? locations[0].Span : null) with { Locations = locations };
    }

    private Reply Navigate(Request request, Lifetime lifetime, string bindingSource)
    {
        if (request.Session != index.Session.ToString() || request.ContextVersion != index.Epoch || request.Target == null)
            return Response(request, "stale", "Result expired; run navigation again");
        if (scripts.CurrentText(request.Path) != request.Text)
            return Response(request, "stale", "Document changed; run navigation again");
        var target = TypeCatalog.Resolve(psi, request.Target);
        if (target == null) return Response(request, "stale", "The determined type is no longer valid in the consuming context; resolve again");
        // A compiled element remains external even if the host has cached a source/decompiled file.
        if (target is not ICompiledElement && target.Module.ContainingProjectModule is IProject
            && target.GetSourceFiles().Any(source => source.IsValid()))
        {
            target.Navigate(true);
            // A command dispatch, not a fabricated claim that the IDE selected the correct source.
            return Response(request, "navigationDispatched", bindingSource + "; Project source navigation delegated to Rider; not proof of source matching a prior binary", target: request.Target);
        }
        var presentation = ExternalTypeNavigation.Navigate(target, () => lifetime.IsAlive
            && request.ContextVersion == index.Epoch && request.Session == index.Session.ToString()
            && scripts.CurrentText(request.Path) == request.Text);
        if (!lifetime.IsAlive || request.ContextVersion != index.Epoch)
            return Response(request, "stale", "Binding context expired during external presentation; resolve again");
        return Response(request, presentation.Status, bindingSource + "; Determined C# binding retained; " + presentation.Reason
            + "; consumer=" + request.Target.Consumer + "; consuming TFM=" + request.Target.Context,
            target: request.Target) with { ExternalPresentation = presentation };
    }
}
