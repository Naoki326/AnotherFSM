using System.Reflection;

namespace StateMachine
{
    public partial class FSMEngine
    {
        public FSMContextFlow<TInput> BindContext<TInput>(Action<FSMContextBindings<TInput>>? configure = null) where TInput : class
            => new FSMContextFlow<TInput>(this, configure);

        public FSMContextFlow<TInput> BindContext<TInput>(string startNode, string endEvent, Action<FSMContextBindings<TInput>>? configure = null) where TInput : class
            => new FSMContextFlow<TInput>(this, configure, (startNode, endEvent));
    }

    /// <summary>A reusable assembly plan, never captures an execution's business objects.</summary>
    public sealed class FSMContextFlow<TInput> where TInput : class
    {
        private readonly FSMEngine engine;
        private readonly List<(IFSMNode Node, ContextSource Source)> bindings = new();
        private readonly (string Name, IFSMNode Node)[] nodes;
        private readonly (string Start, string End)? entry;

        internal FSMContextFlow(FSMEngine engine, Action<FSMContextBindings<TInput>>? configure, (string Start, string End)? entry = null)
        {
            this.engine = engine;
            this.entry = entry;
            if (entry.HasValue && (!engine.ContainsNode(entry.Value.Start) || !engine.ContainsEvent(entry.Value.End)))
                throw new FSMException($"Configured entry {entry.Value.Start} / end event {entry.Value.End} does not exist.");
            engine.ValidateContextNodes();
            nodes = engine.GetNodeNames().Select(name => (name, engine.GetNode(name))).ToArray();
            var configuration = new FSMContextBindings<TInput>();
            configure?.Invoke(configuration);
            var rules = configuration.Rules.Select(r =>
            {
                try
                {
                    var source = ContextSource.Parse(r.Source);
                    source.Validate();
                    if (r.Requirement != null && !r.Requirement.IsAssignableFrom(source.DeclaredType))
                        throw new FSMException($"Source {source} is not compatible with declared requirement {r.Requirement}.");
                    return (r.Node, r.Requirement, Source: source);
                }
                catch (FSMException ex)
                {
                    var affected = r.Node ?? string.Join(", ", engine.GetNodeNames());
                    var requirements = r.Requirement?.ToString() ?? string.Join(", ", engine.OfType<IFSMContextNode>().Select(n => n.ContextType));
                    throw new FSMException($"Node {affected}, requirement {requirements}: {ex.Message}", ex);
                }
            }).ToArray();
            var defaultRequirements = rules.Where(r => r.Node == null).SelectMany(r => r.Requirement != null
                ? new[] { r.Requirement } : r.Source.DeclaredType.GetInterfaces().Concat(new[] { r.Source.DeclaredType })).Distinct();
            foreach (var requirement in defaultRequirements)
            {
                var defaults = rules.Where(r => r.Node == null && (r.Requirement == requirement
                    || r.Requirement == null && requirement.IsAssignableFrom(r.Source.DeclaredType))).ToArray();
                if (defaults.Length > 1)
                {
                    var affected = engine.GetNodeNames().Where(name => ((IFSMContextNode)engine.GetNode(name)).ContextType == requirement);
                    throw new FSMException($"Node {string.Join(", ", affected)} requires {requirement}: duplicate/conflicting defaults {string.Join(", ", defaults.Select(r => r.Source.ToString()))}.");
                }
            }
            foreach (var rule in rules.Where(r => r.Node != null))
            {
                if (!engine.TryGetNode(rule.Node!, out var target))
                    throw new FSMException($"Node {rule.Node} not found; declared requirement {rule.Requirement}, source {rule.Source}.");
                if (target is not IFSMContextNode targetContext || targetContext.ContextType != rule.Requirement)
                    throw new FSMException($"Node {rule.Node}: declared requirement {rule.Requirement} does not match actual {(target as IFSMContextNode)?.ContextType}; source {rule.Source}.");
            }
            foreach (var node in engine)
            {
                if (node is not IFSMContextNode contextual)
                    throw new FSMException($"Node {node.Name} must declare its context requirement.");
                if (contextual.ContextType is not Type requirement) continue;
                var qualifiedName = engine.GetNodeNames().Single(name => ReferenceEquals(engine.GetNode(name), node));
                var overrides = rules.Where(r => r.Node == qualifiedName).Select(r => r.Source).ToArray();
                var defaults = rules.Where(r => r.Node == null && (r.Requirement == requirement
                    || r.Requirement == null && requirement.IsAssignableFrom(r.Source.DeclaredType))).Select(r => r.Source).ToArray();
                var automatic = typeof(TInput).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0
                        && requirement.IsAssignableFrom(p.PropertyType)).Select(p => new ContextSource(p)).ToArray();
                if (overrides.Length > 1 || defaults.Length > 1)
                    throw new FSMException($"Node {qualifiedName} requires {requirement}: duplicate/conflicting sources {string.Join(", ", overrides.Concat(defaults).Select(s => s.ToString()))}.");
                var candidates = overrides.Length > 0 ? overrides : defaults.Length > 0 ? defaults : automatic;
                if (candidates.Length != 1)
                    throw new FSMException($"Node {qualifiedName} requires {requirement}: expected one source in {typeof(TInput)}, found {candidates.Length}: {string.Join(", ", candidates.Select(p => p.ToString()))}.");
                try { candidates[0].Validate(); }
                catch (FSMException ex) { throw new FSMException($"Node {qualifiedName} requires {requirement}: {ex.Message}", ex); }
                bindings.Add((node, candidates[0]));
            }
        }

        private void ValidateGraph()
        {
            if (engine.GetNodeNames().Count() != nodes.Length || nodes.Any(n => !engine.TryGetNode(n.Name, out var actual) || !ReferenceEquals(n.Node, actual)))
                throw new FSMException($"Engine nodes changed ({string.Join(", ", engine.GetNodeNames())}); call BindContext again to validate the complete engine.");
        }

        private string Describe((IFSMNode Node, ContextSource Source) binding)
            => $"Node {nodes.First(n => ReferenceEquals(n.Node, binding.Node)).Name}, requirement {((IFSMContextNode)binding.Node).ContextType}, source {binding.Source}";

        public FSMContextPreparation PrepareDynamic(object? input)
        {
            if (input is not TInput typed)
                throw new FSMException($"Aggregate declared {typeof(TInput)}, actual {input?.GetType().ToString() ?? "null"}; required by {string.Join("; ", bindings.Select(Describe))}.");
            return Prepare(typed);
        }

        public FSMContextPreparation Prepare(TInput input)
        {
            if (input == null) return PrepareDynamic(input);
            ValidateGraph();
            var resolved = new Dictionary<string, object>();
            var values = new List<(IFSMNode Node, object Value)>();
            foreach (var binding in bindings)
            {
                if (!resolved.TryGetValue(binding.Source.Path, out var value))
                {
                    try { value = binding.Source.Resolve(input); }
                    catch (Exception ex)
                    {
                        throw new FSMException($"{Describe(binding)}, actual unavailable: source getter failed: {ex.GetBaseException().Message}", ex);
                    }
                    if (value == null)
                        throw new FSMException($"{Describe(binding)}, actual null: necessary source is null; no candidate fallback.");
                    resolved.Add(binding.Source.Path, value);
                }
                var requirement = ((IFSMContextNode)binding.Node).ContextType!;
                if (!requirement.IsInstanceOfType(value))
                    throw new FSMException($"{Describe(binding)}, actual {value.GetType()}: incompatible source type.");
                values.Add((binding.Node, value));
            }
            return new FSMContextPreparation(engine, nodes.Select(n => n.Node).ToArray(), values, ValidateGraph, entry);
        }
    }

    /// <summary>Fixed references for one execution. Data preparation only; engine occupancy is a separate lifecycle responsibility.</summary>
    public sealed class FSMContextPreparation
    {
        private readonly FSMEngine engine;
        private readonly IFSMNode[] nodes;
        private readonly IReadOnlyList<(IFSMNode Node, object Value)> values;
        private readonly Action validateGraph;
        private readonly (string Start, string End)? entry;
        internal FSMContextPreparation(FSMEngine engine, IFSMNode[] nodes, IReadOnlyList<(IFSMNode Node, object Value)> values, Action validateGraph, (string Start, string End)? entry)
        {
            this.engine = engine;
            this.nodes = nodes;
            this.values = values;
            this.validateGraph = validateGraph;
            this.entry = entry;
        }

        public Task<FSMExecutor> StartAsync(bool isLongRunning = false)
        {
            if (!entry.HasValue) throw new FSMException("No assembled entry; supply startNode/endEvent or configure them in BindContext.");
            return StartAsync(entry.Value.Start, entry.Value.End, isLongRunning);
        }

        public async Task<FSMExecutor> StartAsync(string startNode, string endEvent, bool isLongRunning = false)
        {
            validateGraph();
            var executor = new FSMExecutor(engine.GetNode(startNode), engine.GetEvent(endEvent));
            await executor.StartContextAsync(nodes, values, isLongRunning);
            return executor;
        }
    }
}
