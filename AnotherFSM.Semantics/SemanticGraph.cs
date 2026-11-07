using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using AnotherFSM.Syntax;
using P = AnotherFSM.Semantics.Syntax.StateMachineScriptParser;

namespace AnotherFSM.Semantics;

/// <summary>A factory-bearing Node at its real source file, including an Input's internal Node.</summary>
public sealed record SemanticNode(NodeDeclaration Node, string DocumentId);
/// <summary>Owner-scoped DSL integer index; not a C# enum or a return/yield location.</summary>
public sealed record SemanticBranch(SemanticNode Owner, int Index);

/// <summary>A real UTF-16 source location. Instances are identities, not synthetic source copies.</summary>
public sealed record SemanticLocation(string DocumentId, SourceSpan Span, string Kind, string Label, IReadOnlyList<string> Instances);

/// <summary>Status: resolved = declaration(s); locations = introduction/use positions without a unique
/// declaration (also reference results); metadata = explanatory static metadata; unresolved = no reliable binding.</summary>
public sealed record SemanticQuery(string Status, string Reason, IReadOnlyList<SemanticLocation> Locations);

/// <summary>Immutable static DSL index. Roots have independent build scopes. No runtime graph, factory,
/// business getter or host discovery is executed. Rebuild with new document versions to update results.</summary>
public sealed class SemanticGraph
{
    private sealed class Symbol(string kind, string name, Scope scope)
    {
        public readonly string Kind = kind, Name = name;
        public readonly Scope Scope = scope;
        public readonly List<Occurrence> Uses = new();
        public NodeDeclaration? Node;
        public bool Invalid;
        public int? Index;
        public Symbol? Template;
        public Symbol? Instance;
        public Scope? Body;
        public readonly Dictionary<int, Symbol> BranchEvents = new();
    }
    private sealed record Occurrence(Symbol? Symbol, DocumentAnalysis Doc, SourceSpan Span, string Role, string Reason = "");
    private sealed class Scope(DocumentAnalysis doc, string id, Dictionary<string, Symbol>? registry = null, HashSet<string>? blockedRegistrations = null)
    {
        public readonly DocumentAnalysis Doc = doc;
        public readonly string Id = id;
        public readonly Dictionary<string, Symbol> Nodes = new(DeclarationRules.NameComparer);
        public readonly Dictionary<string, Symbol> Events = new(DeclarationRules.NameComparer);
        public readonly Dictionary<string, Symbol> Namespaces = new(DeclarationRules.NameComparer);
        public readonly Dictionary<string, Symbol> Instances = new(DeclarationRules.ModuleComparer);
        public readonly Dictionary<string, Symbol> Registry = registry ?? new(DeclarationRules.ModuleComparer);
        public readonly HashSet<string> BlockedRegistrations = blockedRegistrations ?? new(DeclarationRules.ModuleComparer);
        public bool ImportFailure;
        public readonly List<(ParserRuleContext Context, string Prefix)> Pending = new();
        public readonly Dictionary<(Symbol Node, Symbol Event, Symbol? Instance), Occurrence[]> Connections = new();
        public Symbol? Module;
    }
    private readonly Dictionary<string, DocumentAnalysis> documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Occurrence> occurrences = new();
    private readonly List<Symbol> symbols = new();
    private readonly Dictionary<string, Symbol> moduleSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loading = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, string, DocumentAnalysis?> resolveImport = null!;
    private readonly CancellationToken cancellation;
    private readonly Action? checkHostCancellation;
    private SemanticGraph(CancellationToken cancellation, Action? checkHostCancellation)
        => (this.cancellation, this.checkHostCancellation) = (cancellation, checkHostCancellation);
    /// <summary>Roots plus resolved transitive imports, retaining exact snapshot versions and canonical source IDs.</summary>
    public IReadOnlyList<DocumentAnalysis> Documents => Array.AsReadOnly(documents.Values.ToArray());

    /// <summary>Build from already parsed snapshots. Each root has an independent registration environment.
    /// The resolver receives the importing file's real ID and original module name (never its alias).
    /// It alone supplies file access/mappings; null means unavailable. Windows path IDs are slash-normalized
    /// and compared case-insensitively; the first supplied snapshot for an entity wins (roots precede imports).
    /// Cancellation and host cancellation exceptions propagate. No whole-solution name fallback is performed.</summary>
    public static SemanticGraph Build(IEnumerable<DocumentAnalysis> roots,
        Func<string, string, DocumentAnalysis?> resolveImport, CancellationToken cancellation = default, Action? checkHostCancellation = null,
        Func<string, IReadOnlyList<string>>? registeredModules = null)
    {
        var graph = new SemanticGraph(cancellation, checkHostCancellation) { resolveImport = resolveImport };
        var rootDocuments = new List<DocumentAnalysis>();
        foreach (var root in roots)
        {
            graph.Check();
            if (graph.documents.TryAdd(PathKey(root.Document.Id), root)) rootDocuments.Add(root);
        }
        foreach (var doc in rootDocuments)
        {
            graph.Check();
            var scope = new Scope(doc, doc.Document.Id);
            graph.Process(scope, registeredModules?.Invoke(doc.Document.Id));
        }
        graph.BindNamespacePrefixes();
        return graph;
    }
    private void BindNamespacePrefixes()
    {
        foreach (var occurrence in occurrences.ToArray())
        {
            Check();
            if (occurrence.Symbol is not { Kind: "node" or "event" or "instance" } symbol
                || IsInvalid(symbol) || occurrence.Role == "factoryKey") continue;
            var text = occurrence.Doc.Document.Text.Substring(occurrence.Span.Start, occurrence.Span.Length);
            foreach (var ns in symbol.Scope.Namespaces.Values)
                if (text.StartsWith(ns.Name + ".", StringComparison.Ordinal))
                    Add(ns, symbol.Scope, new SourceSpan(occurrence.Span.Start, ns.Name.Length), "use");
        }
    }
    private static string PathKey(string id) => id.Replace('\\', '/');
    private bool Process(Scope scope, IReadOnlyList<string>? registered = null)
    {
        if (!loading.Add(PathKey(scope.Doc.Document.Id))) return false;
        try
        {
            if (registered != null)
                foreach (var group in registered.GroupBy(name => name, DeclarationRules.ModuleComparer))
                {
                    var name = group.Key;
                    var (_, reason) = LoadModule(scope, name);
                    if (group.Count() > 1 || reason.Length > 0)
                    { scope.BlockedRegistrations.Add(name); scope.ImportFailure = true; }
                }
            if (scope.Doc.SyntaxTree != null) Collect(scope, scope.Doc.SyntaxTree, "");
            Bind(scope);
            foreach (var instance in symbols.Where(s => s.Kind == "instance" && ReferenceEquals(s.Scope.Registry, scope.Registry)))
                if (instance.Template != null && scope.BlockedRegistrations.Contains(instance.Template.Name)) instance.Invalid = true;
            return !scope.ImportFailure;
        }
        finally { loading.Remove(PathKey(scope.Doc.Document.Id)); }
    }
    private void Check() { cancellation.ThrowIfCancellationRequested(); checkHostCancellation?.Invoke(); }
    private static SourceSpan Span(IToken token) => new(token.StartIndex, token.StopIndex - token.StartIndex + 1);
    private static SourceSpan Span(ITerminalNode token) => Span(token.Symbol);
    private bool Reliable(Scope scope, ParserRuleContext context)
        => !scope.Doc.ErrorOffsets.Any(p => p >= context.Start.StartIndex && p <= context.Stop.StopIndex)
            && !context.children.OfType<ITerminalNode>().Any(t => t is IErrorNode || t.Symbol.TokenIndex < 0);
    private Symbol Get(Dictionary<string, Symbol> table, string kind, string name, Scope scope)
    {
        if (!table.TryGetValue(name, out var symbol))
        { symbol = new Symbol(kind, name, scope); table.Add(name, symbol); symbols.Add(symbol); }
        return symbol;
    }
    private void Add(Symbol? symbol, Scope scope, SourceSpan span, string role, string reason = "")
    {
        var occurrence = new Occurrence(symbol, scope.Doc, span, role, reason);
        occurrences.Add(occurrence);
        symbol?.Uses.Add(occurrence);
    }
    private void Collect(Scope scope, IParseTree tree, string prefix)
    {
        Check();
        switch (tree)
        {
            case P.NamespaceContext ns:
                var open = ns.children.OfType<ITerminalNode>().FirstOrDefault(t => t.GetText() == "{");
                if (open == null || open is IErrorNode || open.Symbol.TokenIndex < 0 || ns.STRING() is IErrorNode || ns.STRING().Symbol.TokenIndex < 0
                    || scope.Doc.ErrorOffsets.Any(p => p >= ns.Start.StartIndex && p <= open.Symbol.StartIndex)) return;
                var name = ns.STRING().GetText();
                Add(Get(scope.Namespaces, "namespace", name, scope), scope, Span(ns.STRING()), "declaration");
                foreach (var expression in ns.expression()) Collect(scope, expression, name + ".");
                return;
            case P.Import_statementContext import: Import(scope, import); return;
            case P.Module_statementContext module: DefineModule(scope, module); return;
            case P.Input_declarationContext input:
                if (Reliable(scope, input)) foreach (var token in input.STRING())
                    Add(Get(scope.Nodes, "node", token.GetText(), scope), scope, Span(token), "input");
                return;
            case P.Output_declarationContext output:
                if (Reliable(scope, output)) foreach (var token in output.STRING())
                    Add(Get(scope.Events, "event", token.GetText(), scope), scope, Span(token), "output");
                return;
            case P.Terminal_declarationContext:
                if (scope.Module != null) scope.Module.Invalid = true;
                return;
            case P.DefStateContext state: Define(scope, state, state.STRING(), state.state_branch(), prefix); return;
            case P.DefState2Context state: Define(scope, state, state.STRING(), state.state_branch(), prefix); return;
            case P.DefEventContext evt:
                if (Reliable(scope, evt)) Add(Get(scope.Events, "event", prefix + evt.STRING().GetText(), scope), scope, Span(evt.STRING()), "declaration");
                return;
            case P.DefTransitionContext transition:
                scope.Pending.Add((transition, prefix)); return;
            case P.ModuleOutputTransitionContext transition:
                scope.Pending.Add((transition, prefix)); return;
            case P.Module_instanceContext instance: DefineInstance(scope, instance, prefix); return;
        }
        for (var i = 0; i < tree.ChildCount; i++) Collect(scope, tree.GetChild(i), prefix);
    }
    private void Define(Scope scope, ParserRuleContext context, ITerminalNode[] names, P.State_branchContext[] branches, string prefix)
    {
        var node = scope.Doc.SyntaxNodes.FirstOrDefault(n => n.NameSpan == Span(names[0]));
        if (node == null) return;
        var symbol = Get(scope.Nodes, "node", node.Name, scope);
        if (symbol.Node != null) symbol.Invalid = true;
        symbol.Node = node;
        symbol.Invalid |= branches.Any(b => b is P.NamedOutputMapDefContext or P.RemovedCompositionDefContext);
        Add(symbol, scope, node.NameSpan, "declaration");
        if (!node.ImplicitKey) Add(symbol, scope, node.KeySpan, "factoryKey");
        foreach (var branch in branches)
        {
            Check();
            if (!Reliable(scope, branch)) continue;
            IToken? index = null;
            ITerminalNode? evt = null;
            if (branch is P.DefBranchContext b) { index = b.INT().Symbol; evt = b.STRING(); }
            if (branch is P.DefBranch2Context b2) { index = b2.branch_type; evt = b2.STRING(); }
            if (index == null || evt == null || !int.TryParse(index.Text, out _) && index.Type == P.INT) continue;
            var value = DeclarationRules.BranchIndex(index.Text);
            var key = node.Name + "#" + value;
            var bs = symbols.FirstOrDefault(s => s.Scope == scope && s.Kind == "branch" && s.Name == key);
            if (bs == null) { bs = new Symbol("branch", key, scope) { Node = node, Index = value }; symbols.Add(bs); }
            Add(bs, scope, Span(index), "branch");
            var eventSymbol = Get(scope.Events, "event", prefix + evt.GetText(), scope);
            symbol.BranchEvents[value] = eventSymbol;
            Add(eventSymbol, scope, Span(evt), "use");
        }
    }
    private void ValidateInstances(Scope scope)
    {
        var nodeOwners = new Dictionary<string, Symbol>(DeclarationRules.NameComparer);
        var eventOwners = new Dictionary<string, Symbol>(DeclarationRules.NameComparer);
        foreach (var instance in scope.Instances.Values)
        {
            Check();
            foreach (var node in scope.Nodes.Values.Where(n => DeclarationRules.ModuleComparer.Equals(n.Name, instance.Name)))
                node.Invalid = instance.Invalid = true;
            if (instance.Body == null) continue;
            foreach (var node in instance.Body.Nodes.Values)
            {
                var name = instance.Name + "." + node.Name;
                if (scope.Nodes.TryGetValue(name, out var ordinary)) ordinary.Invalid = instance.Invalid = true;
                if (scope.Instances.TryGetValue(name, out var nested)) nested.Invalid = instance.Invalid = true;
                if (nodeOwners.TryGetValue(name, out var other)) other.Invalid = instance.Invalid = true;
                nodeOwners[name] = instance;
            }
            foreach (var evt in instance.Body.Events.Values)
            {
                var name = instance.Name + "." + evt.Name;
                if (scope.Events.TryGetValue(name, out var ordinary)) ordinary.Invalid = instance.Invalid = true;
                if (eventOwners.TryGetValue(name, out var other)) other.Invalid = instance.Invalid = true;
                eventOwners[name] = instance;
            }
        }
    }
    private void Bind(Scope scope)
    {
        ValidateInstances(scope);
        foreach (var (context, prefix) in scope.Pending)
        {
            Check();
            if (!Reliable(scope, context)) continue;
            var start = occurrences.Count;
            if (context is P.DefTransitionContext transition)
            {
                var names = transition.STRING();
                var eventName = prefix + names[0].GetText();
                var ownedEvent = scope.Instances.Values.Any(i => i.Body?.Events.Keys.Any(e => i.Name + "." + e == eventName) == true);
                Add(ownedEvent ? null : Get(scope.Events, "event", eventName, scope), scope, Span(names[0]), "use", "Module outputs require public short wiring, not an ordinary event transition.");
                BindNode(scope, names[1], prefix, source: true);
                BindNode(scope, names[2], prefix);
            }
            else if (context is P.ModuleOutputTransitionContext output)
            {
                var names = output.STRING();
                BindInterface(scope, names[0], prefix, isOutput: true);
                if (scope.Module != null) Add(null, scope, Span(names[0]), "use", "Nested module Output wiring is not supported.");
                BindNode(scope, names[1], prefix);
            }
            var current = occurrences.Skip(start).ToArray();
            if (current.Any(o => o.Symbol == null || IsInvalid(o.Symbol)))
                Reject(current, "Invalid connection: missing, private, ambiguous or invalid endpoint.");
            else
            {
                var evt = current.Select(o => o.Symbol!).First(s => s.Kind == "event");
                var publishers = context is P.DefTransitionContext
                    ? current.Select(o => o.Symbol!).Where(s => s.Kind == "node").Take(1)
                    : Original(evt).Scope.Nodes.Values.Where(n => !IsInvalid(n) && n.BranchEvents.Values.Contains(Original(evt)));
                foreach (var publisher in publishers)
                {
                    var key = (publisher, Original(evt), evt.Instance);
                    if (scope.Connections.TryGetValue(key, out var previous))
                    {
                        Reject(previous, "Conflicting source + Event connection.");
                        Reject(current, "Conflicting source + Event connection.");
                    }
                    else scope.Connections.Add(key, current);
                }
            }
        }
    }
    private void Reject(IEnumerable<Occurrence> rejected, string reason)
    {
        foreach (var occurrence in rejected)
        {
            occurrence.Symbol?.Uses.Remove(occurrence);
            var index = occurrences.IndexOf(occurrence);
            if (index >= 0) occurrences[index] = occurrence with { Symbol = null, Reason = reason };
        }
    }
    private void BindNode(Scope scope, ITerminalNode token, string prefix, bool source = false)
    {
        bool Expanded(string name) => scope.Instances.Values.Any(i => i.Body != null
            && DeclarationRules.IsQualifiedInterface(name, i.Name, i.Body.Nodes.Keys));
        var name = DeclarationRules.QualifyNodeReference(prefix, token.GetText(),
            n => scope.Instances.ContainsKey(n) || scope.Nodes.ContainsKey(n) || Expanded(n),
            n => scope.Instances.ContainsKey(n) || Expanded(n),
            n => scope.Instances.Values.Any(i => n.StartsWith(i.Name + ".", StringComparison.OrdinalIgnoreCase)));
        if (scope.Instances.TryGetValue(name, out var instance))
        {
            // Unresolved Inputs still count as declared interfaces; damage cannot authorize omission.
            var inputs = instance.Body?.Nodes.Values.Where(n => n.Uses.Any(o => o.Role == "input")).ToArray();
            var uniqueInput = inputs?.Length == 1 && !IsInvalid(inputs[0]);
            Add(source || !uniqueInput ? null : instance, scope, Span(token), "use", "Instance is not a source Node; multi/zero Input targets require an explicit public Input.");
            // The same real token uses both the instance and its omitted unique public Input.
            // Keep the instance occurrence first so ordinary navigation/implementation never invents an instance Node.
            if (!source && uniqueInput) Add(Proxy(inputs![0], instance), scope, Span(token), "use");
            return;
        }
        scope.Nodes.TryGetValue(name, out var symbol);
        if (symbol == null && !source && BindInterface(scope, token, prefix, isOutput: false, resolvedName: name)) return;
        Add(symbol, scope, Span(token), "use", "Node is missing, private or ambiguous.");
    }
    private void Import(Scope scope, P.Import_statementContext context)
    {
        if (!Reliable(scope, context)) return;
        var names = context.STRING();
        var name = names[0].GetText();
        var (module, reason) = LoadModule(scope, name);
        if (reason.Length > 0 || module?.Invalid == true || scope.BlockedRegistrations.Contains(name))
        {
            scope.ImportFailure = true;
            scope.BlockedRegistrations.Add(name);
            Add(null, scope, Span(names[0]), "import", reason.Length > 0 ? reason : "Imported Module is invalid.");
            if (names.Length > 1) Add(null, scope, Span(names[1]), "declaration", reason);
            return;
        }
        Add(module, scope, Span(names[0]), "import");
        if (names.Length == 1 || module == null) return;
        // Every alias targets the canonical Module, while its declaration/use identity stays local.
        // This also makes an import of an existing alias a registry lookup, never an Alias.fsm guess.
        var targetModule = module.Kind == "alias" ? module.Template! : module;
        var alias = new Symbol("alias", names[1].GetText(), scope) { Template = targetModule, Body = targetModule.Body };
        symbols.Add(alias);
        Add(alias, scope, Span(names[1]), "declaration");
        if (scope.Registry.TryGetValue(alias.Name, out var existing) && (existing.Kind == "alias" ? existing.Template : existing) != targetModule)
        { scope.BlockedRegistrations.Add(alias.Name); alias.Invalid = true; }
        scope.Registry[alias.Name] = alias;
    }
    private (Symbol? Module, string Reason) LoadModule(Scope scope, string name)
    {
        if (scope.Registry.TryGetValue(name, out var known)) return (known, "");
        Check();
        var imported = resolveImport(scope.Doc.Document.Id, name);
        if (imported == null) return (null, "Import target unavailable: " + name + ".fsm (requires the actual importing document directory or explicit host mapping).");
        var key = PathKey(imported.Document.Id);
        if (!documents.TryGetValue(key, out var actual)) documents.Add(key, actual = imported);
        if (loading.Contains(key)) return (null, "Circular import: " + name);
        var importedScope = new Scope(actual, actual.Document.Id, scope.Registry, scope.BlockedRegistrations);
        if (!Process(importedScope)) return (null, "Import dependency is missing, damaged or cyclic: " + name);
        if (scope.Registry.TryGetValue(name, out var module)) return (module, "");
        // Runtime registration also accepts a plain script as a file-backed template.
        var sourceId = key + ":file-template:" + name.ToUpperInvariant();
        if (!moduleSources.TryGetValue(sourceId, out module))
        {
            module = new Symbol("module", name, importedScope) { Body = importedScope };
            symbols.Add(module);
            moduleSources.Add(sourceId, module);
            importedScope.Module = module;
            Add(module, importedScope, new SourceSpan(0, 0), "fileTarget");
        }
        scope.Registry[name] = module;
        return (module, "");
    }
    private void DefineModule(Scope parent, P.Module_statementContext context)
    {
        var names = context.STRING();
        var open = context.children.OfType<ITerminalNode>().FirstOrDefault(t => t.GetText() == "{");
        if (open == null || open is IErrorNode || open.Symbol.TokenIndex < 0 || names.Any(n => n is IErrorNode || n.Symbol.TokenIndex < 0)
            || parent.Doc.ErrorOffsets.Any(p => p >= context.Start.StartIndex && p <= open.Symbol.StartIndex)) return;
        var name = DeclarationRules.ModuleRegistrationName(names[0].GetText(), names.Length > 1 ? names[1].GetText() : null);
        var sourceId = PathKey(parent.Doc.Document.Id) + ":module:" + context.Start.StartIndex;
        if (moduleSources.TryGetValue(sourceId, out var cached))
        {
            if (parent.Registry.TryGetValue(name, out var collision) && collision != cached) parent.BlockedRegistrations.Add(name);
            parent.Registry[name] = cached;
            return;
        }
        var body = new Scope(parent.Doc, sourceId);
        var module = new Symbol("module", name, parent) { Body = body };
        symbols.Add(module);
        moduleSources.Add(sourceId, module);
        body.Module = module;
        Add(module, parent, Span(names[^1]), "declaration");
        if (names.Length > 1) Add(module, parent, Span(names[0]), "moduleOriginalName");
        Collect(body, context.module_body(), "");
        Bind(body);
        foreach (var input in body.Nodes.Values)
            if (input.Uses.Count(o => o.Role == "input") > 1 || input.Uses.Any(o => o.Role == "input") && input.Node == null) input.Invalid = true;
        foreach (var output in body.Events.Values)
            if (output.Uses.Count(o => o.Role == "output") > 1) output.Invalid = true;
        if (parent.Registry.ContainsKey(name)) parent.BlockedRegistrations.Add(name);
        parent.Registry[name] = module;
    }
    private void DefineInstance(Scope scope, P.Module_instanceContext context, string prefix)
    {
        var names = context.STRING();
        if (names.Length != 2 || !Reliable(scope, context)) return;
        if (scope.Module != null) { scope.Module.Invalid = true; return; }
        scope.Registry.TryGetValue(names[0].GetText(), out var module);
        var unavailable = module == null || scope.BlockedRegistrations.Contains(names[0].GetText());
        Add(unavailable ? null : module, scope, Span(names[0]), "use", "Module must be unambiguous and declared or imported before use.");
        var instance = Get(scope.Instances, "instance", prefix + names[1].GetText(), scope);
        if (instance.Uses.Any(o => o.Role == "declaration")) instance.Invalid = true;
        instance.Template = module?.Kind == "alias" ? module.Template : module;
        instance.Body = instance.Template?.Body;
        instance.Invalid |= unavailable || module?.Invalid == true || context.state_branch().Any(b => b is not P.PosDefContext and not P.ColorDefContext and not P.FlowIDDefContext);
        Add(instance, scope, Span(names[1]), "declaration");
    }
    private Symbol Proxy(Symbol template, Symbol instance)
    {
        var proxy = symbols.FirstOrDefault(s => s.Template == template && s.Instance == instance);
        if (proxy != null) return proxy;
        proxy = new Symbol(template.Kind, template.Name, template.Scope) { Template = template, Instance = instance, Node = template.Node };
        symbols.Add(proxy);
        return proxy;
    }
    private bool BindInterface(Scope scope, ITerminalNode token, string prefix, bool isOutput, string? resolvedName = null)
    {
        var reference = token.GetText();
        Symbol? Owner(string name)
        {
            var owners = scope.Instances.Values.Where(i => i.Body != null && DeclarationRules.IsQualifiedInterface(name, i.Name,
                (isOutput ? i.Body.Events.Values : i.Body.Nodes.Values).Where(s => s.Uses.Any(o => o.Role == (isOutput ? "output" : "input"))).Select(s => s.Name))).ToArray();
            return owners.Length == 1 ? owners[0] : null;
        }
        var name = resolvedName ?? prefix + reference;
        var owner = Owner(name);
        if (owner == null && resolvedName == null) { name = reference; owner = Owner(name); }
        if (owner == null || owner.Invalid || owner.Body == null)
        {
            if (isOutput) Add(null, scope, Span(token), "use", "No valid public Output or publisher.");
            return false;
        }
        var member = name.Substring(owner.Name.Length + 1);
        var symbol = (isOutput ? owner.Body.Events : owner.Body.Nodes)[member];
        if (symbol.Invalid || isOutput && !owner.Body.Nodes.Values.Any(n => !IsInvalid(n) && n.BranchEvents.Values.Contains(symbol)))
        { Add(null, scope, Span(token), "use", "Interface is invalid or Output has no real publisher."); return true; }
        // A Namespace prefix added implicitly is not present in the token's source span.
        var explicitOwnerLength = owner.Name.Length - (name == prefix + reference ? prefix.Length : 0);
        Add(owner, scope, new SourceSpan(token.Symbol.StartIndex, explicitOwnerLength), "use");
        Add(Proxy(symbol, owner), scope, new SourceSpan(token.Symbol.StartIndex + explicitOwnerLength + 1, member.Length), "use");
        return true;
    }
    private Occurrence? At(string documentId, int offset)
        => occurrences.Where(o => StringComparer.OrdinalIgnoreCase.Equals(PathKey(o.Doc.Document.Id), PathKey(documentId)) && o.Span.Contains(offset))
            .OrderBy(o => o.Span.Length).FirstOrDefault();
    private static SemanticQuery Unresolved(string reason) => new("unresolved", reason, Array.Empty<SemanticLocation>());
    private static IReadOnlyList<SemanticLocation> Locations(IEnumerable<Occurrence> source, IEnumerable<string>? instances = null)
        => Array.AsReadOnly(source.GroupBy(o => (o.Doc.Document.Id, o.Span))
            .Select(g => new SemanticLocation(g.Key.Id, g.Key.Span, g.First().Symbol!.Kind, g.First().Symbol!.Name,
                Array.AsReadOnly((g.Any(o => o.Symbol?.Instance != null)
                    ? g.Select(o => o.Symbol?.Instance).OfType<Symbol>().Select(InstanceId)
                    : instances ?? Array.Empty<string>()).Distinct().ToArray()))).ToArray());
    private static string InstanceId(Symbol instance) => instance.Scope.Id + "::" + instance.Name;
    private static Symbol Original(Symbol symbol) => symbol.Instance != null ? symbol.Template! : symbol;
    private static bool IsInvalid(Symbol symbol) => symbol.Invalid || symbol.Scope.Module?.Invalid == true
        || symbol.Kind == "branch" && (symbol.Node == null || !symbol.Scope.Nodes.TryGetValue(symbol.Node.Name, out var owner) || owner.Invalid)
        || symbol.Instance != null && IsInvalid(symbol.Instance)
        || symbol.Template != null && IsInvalid(symbol.Template);
    public SemanticQuery Navigate(string documentId, int offset, string operation = "declaration")
    {
        var occurrence = At(documentId, offset);
        var symbol = occurrence?.Symbol;
        if (symbol == null || IsInvalid(symbol)) return Unresolved(occurrence?.Reason is { Length: > 0 } reason ? reason : "No reliable unique symbol at this position.");
        if (symbol.Kind == "branch") return new("metadata", $"DSL branch index {symbol.Index}; no explicit C# metadata location supplied.", Array.Empty<SemanticLocation>());
        if (operation == "entry")
        {
            if (symbol.Kind != "instance") return Unresolved("Select a Module Instance with a unique public Input, not a Module declaration.");
            var inputs = symbol.Body?.Nodes.Values.Where(n => n.Uses.Any(o => o.Role == "input")).ToArray();
            return inputs?.Length == 1 && !IsInvalid(inputs[0]) ? new("resolved", "", Locations(inputs[0].Uses.Where(o => o.Role == "input")))
                : Unresolved("Select an explicit public Input; there is no unique reliable entry.");
        }
        if (operation == "moduleDefinition" && symbol.Kind is "alias" or "instance" && symbol.Template != null) symbol = symbol.Template;
        var preferInterface = symbol.Instance != null || occurrence!.Role is "input" or "output";
        symbol = Original(symbol);
        if (operation == "internal")
        {
            if (symbol.Kind == "node") return new("resolved", "", Locations(symbol.Uses.Where(o => o.Role == "declaration")));
            if (symbol.Kind == "event")
            {
                var publishers = symbol.Scope.Nodes.Values.Where(n => !n.Invalid && n.BranchEvents.Values.Contains(symbol)).ToArray();
                return publishers.Length > 0 ? new("resolved", "", Locations(publishers.SelectMany(n => n.Uses.Where(o => o.Role == "declaration"))))
                    : new("locations", "No real Branch publisher; Event use positions only.", Locations(symbol.Uses.Where(o => o.Role == "use")));
            }
        }
        var interfaces = symbol.Uses.Where(o => o.Role is "input" or "output").ToArray();
        var declarations = preferInterface && interfaces.Length > 0 ? interfaces : symbol.Uses.Where(o => o.Role is "declaration" or "fileTarget" || symbol.Kind == "event" && o.Role == "output").ToArray();
        return declarations.Length > 0 ? new("resolved", "", Locations(declarations))
            : new("locations", "Implicit Event: introduction/use locations, not a unique declaration.", Locations(symbol.Uses));
    }
    /// <summary>Real use positions only, excluding symbol/interface/alias declarations and factory keys.
    /// Template queries aggregate instance uses; instance interface queries include shared internal uses
    /// plus that instance's external uses. Branch queries include same-owner, same-index Branch tokens.</summary>
    public SemanticQuery FindReferences(string documentId, int offset, bool template = false)
    {
        var occurrence = At(documentId, offset);
        if (occurrence?.Symbol is not { Invalid: false } symbol) return Unresolved(occurrence?.Reason ?? "No reliable symbol.");
        if (IsInvalid(symbol)) return Unresolved("Invalid template or instance.");
        if (template && symbol.Kind == "instance" && symbol.Template != null) symbol = symbol.Template;
        var original = Original(symbol);
        var proxies = symbols.Where(s => s.Template == original && s.Instance is { Invalid: false }).ToArray();
        var templateInstances = symbols.Where(s => s.Kind == "instance" && s.Template == original.Scope.Module && !IsInvalid(s));
        var instanceIds = symbol.Instance != null && !template ? new[] { InstanceId(symbol.Instance) }
            : templateInstances.Select(InstanceId).Concat(proxies.Select(s => InstanceId(s.Instance!))).Distinct().ToArray();
        IEnumerable<Occurrence> external = symbol.Instance != null && !template ? symbol.Uses : proxies.SelectMany(s => s.Uses);
        if (symbol.Kind == "module") external = external.Concat(symbols.Where(s => s.Kind == "alias" && s.Template == symbol && !s.Invalid).SelectMany(s => s.Uses));
        return new("locations", "", Locations(original.Uses.Concat(external).Where(o => o.Role is "use" or "import" or "branch"), instanceIds));
    }
    /// <summary>Returns only reliable Branch syntax with a unique owning Node. Hosts may attach explicitly supplied metadata.</summary>
    public SemanticBranch? FindBranchSource(string documentId, int offset)
    {
        var symbol = At(documentId, offset)?.Symbol;
        if (symbol is not { Kind: "branch", Index: { } index, Node: { } node }) return null;
        if (!symbol.Scope.Nodes.TryGetValue(node.Name, out var owner) || IsInvalid(owner)) return null;
        return new SemanticBranch(new SemanticNode(node, node.DocumentId), index);
    }
    /// <summary>Like FindNode, with the real source document ID for cross-file host binding.</summary>
    public SemanticNode? FindNodeSource(string documentId, int offset)
    {
        var node = FindNode(documentId, offset);
        return node == null ? null : new SemanticNode(node, node.DocumentId);
    }
    /// <summary>Finds Node name/key and legal Node/Input references. Instances, Events and Outputs have no inherent factory type.</summary>
    public NodeDeclaration? FindNode(string documentId, int offset)
    {
        var symbol = At(documentId, offset)?.Symbol;
        return symbol is { Kind: "node" } && !IsInvalid(symbol) ? symbol.Node : null;
    }
}
