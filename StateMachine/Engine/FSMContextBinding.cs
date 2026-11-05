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
            engine.ValidateExecutionNodes();
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
                var defaults = rules.Where(r => MatchesDefault(r, requirement)).ToArray();
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
                var defaults = rules.Where(r => MatchesDefault(r, requirement)).Select(r => r.Source).ToArray();
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

        private static bool MatchesDefault((string? Node, Type? Requirement, ContextSource Source) rule, Type requirement)
            => rule.Node == null && (rule.Requirement == requirement
                || rule.Requirement == null && requirement.IsAssignableFrom(rule.Source.DeclaredType));

        private void ValidateGraph()
        {
            if (engine.GetNodeNames().Count() != nodes.Length || nodes.Any(n => !engine.TryGetNode(n.Name, out var actual) || !ReferenceEquals(n.Node, actual)))
                throw new FSMException($"Engine nodes changed ({string.Join(", ", engine.GetNodeNames())}); call BindContext again to validate the complete engine.");
            engine.ValidateExecutionNodes();
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
            var owner = new object();
            engine.AcquireExecution(owner);
            try
            {
                ValidateGraph();
                var resolved = new Dictionary<ContextSource, object>();
                var values = new List<(IFSMNode Node, object Value)>();
                foreach (var binding in bindings)
                {
                    if (!resolved.TryGetValue(binding.Source, out var value))
                    {
                        try { value = binding.Source.Resolve(input); }
                        catch (Exception ex)
                        {
                            throw new FSMException($"{Describe(binding)}, actual unavailable: source getter failed: {ex.GetBaseException().Message}", ex);
                        }
                        if (value == null)
                            throw new FSMException($"{Describe(binding)}, actual null: necessary source is null; no candidate fallback.");
                        resolved.Add(binding.Source, value);
                    }
                    var requirement = ((IFSMContextNode)binding.Node).ContextType!;
                    if (!requirement.IsInstanceOfType(value))
                        throw new FSMException($"{Describe(binding)}, actual {value.GetType()}: incompatible source type.");
                    values.Add((binding.Node, value));
                }
                return new FSMContextPreparation(engine, nodes.Select(n => n.Node).ToArray(), values, ValidateGraph, entry, owner);
            }
            catch { engine.ReleaseExecution(owner); throw; }
        }
    }

    /// <summary>Fixed references and exclusive engine ownership for one execution. Dispose an abandoned preparation explicitly.</summary>
    public sealed class FSMContextPreparation : IDisposable, IFSMEventSharingExecution
    {
        private readonly FSMEngine engine;
        private readonly IFSMNode[] nodes;
        private readonly IReadOnlyList<(IFSMNode Node, object Value)> values;
        private readonly Action validateGraph;
        private readonly (string Start, string End)? entry;
        private readonly object owner;
        internal FSMContextPreparation(FSMEngine engine, IFSMNode[] nodes, IReadOnlyList<(IFSMNode Node, object Value)> values, Action validateGraph, (string Start, string End)? entry, object owner)
        {
            this.engine = engine;
            this.nodes = nodes;
            this.values = values;
            this.validateGraph = validateGraph;
            this.entry = entry;
            this.owner = owner;
            Member.Preparation = this;
        }

        internal FSMEventSharing.Member Member { get; } = new FSMEventSharing.Member();
        internal string Description => $"entry {(selectedEntry ?? entry)?.Start ?? "<unset>"}, engine {System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(engine)}";
        private (string Start, string End)? selectedEntry;
        private IReadOnlyList<FSMEvent>? selectedEndEvents;
        private Action<FSMExecutor>? beforeStart;
        private IDisposable? receiverResource;
        // Narrow execution-receiver preparation seam, not a public messaging extension point.
        internal Func<IDisposable>? ReceiverPreparation { get; set; }

        private readonly object sync = FSMEventSharing.Sync;
        private bool started;
        private bool released;

        public void Dispose()
        {
            lock (sync)
            {
                if (started || released) return;
                if (Member.Starting) throw new FSMException("Execution is being started; retry release after startup completes.");
                released = true;
                FSMEventSharing.Exit(Member);
                engine.ReleaseExecution(owner);
            }
        }

        public Task<FSMExecutor> StartAsync(bool isLongRunning = false)
        {
            if (!(selectedEntry ?? entry).HasValue) throw new FSMException("No assembled entry; supply startNode/endEvent or configure them in BindContext.");
            return Task.FromResult(FSMEventSharing.Start(new[] { this }, isLongRunning)[0]);
        }

        public Task<FSMExecutor> StartAsync(string startNode, string endEvent, bool isLongRunning = false)
        {
            WithEntry(startNode, endEvent);
            return StartAsync(isLongRunning);
        }

        /// <summary>Install observers before any node runs; all end events belong to this execution.</summary>
        public Task<FSMExecutor> StartAsync(string startNode, IEnumerable<string> endEvents, Action<FSMExecutor>? beforeStart = null, bool isLongRunning = false)
            => StartAsync(startNode, endEvents.Select(engine.GetEvent), beforeStart, isLongRunning);

        public Task<FSMExecutor> StartAsync(string startNode, IEnumerable<FSMEvent> endEvents, Action<FSMExecutor>? beforeStart = null, bool isLongRunning = false)
        {
            var ends = (endEvents ?? throw new FSMException("End events are required.")).ToArray();
            lock (sync)
            {
                if (started || released || Member.Starting) throw new FSMException("This execution preparation is already started, starting or released.");
                if (!engine.ContainsNode(startNode) || ends.Length == 0 || ends.Any(e => e == null))
                    throw new FSMException($"Entry {startNode} requires an existing node and at least one non-null end event.");
                selectedEntry = (startNode, ends[0].EventName);
                selectedEndEvents = ends;
                this.beforeStart = beforeStart;
            }
            return Task.FromResult(FSMEventSharing.Start(new[] { this }, isLongRunning)[0]);
        }

        /// <summary>Select an entry without starting; useful for coordinated preparations from an unconfigured flow.</summary>
        public FSMContextPreparation WithEntry(string startNode, string endEvent)
        {
            lock (sync)
            {
                if (started || released || Member.Starting) throw new FSMException("This execution preparation is already started, starting or released.");
                if (!engine.ContainsNode(startNode) || !engine.ContainsEvent(endEvent))
                    throw new FSMException($"Entry {startNode} / end event {endEvent} does not exist.");
                selectedEntry = (startNode, endEvent);
                selectedEndEvents = null;
                beforeStart = null;
                return this;
            }
        }

        internal void ValidateStart(bool reserved = false)
        {
            if (started || released || Member.Starting && !reserved) throw new FSMException($"This execution preparation ({Description}) is already started, starting or released.");
            validateGraph();
            var configured = selectedEntry ?? entry ?? throw new FSMException("No assembled entry; configure startNode/endEvent before coordinated Start.");
            if (!engine.ContainsNode(configured.Start) || selectedEndEvents == null && !engine.ContainsEvent(configured.End))
                throw new FSMException($"Entry {configured.Start} / end event {configured.End} does not exist.");
        }

        internal FSMExecutor CreateExecutor()
        {
            var configured = (selectedEntry ?? entry)!.Value;
            return new FSMExecutor(engine.GetNode(configured.Start), selectedEndEvents ?? new[] { engine.GetEvent(configured.End) });
        }

        internal void PrepareReceiver(FSMExecutor executor, bool isLongRunning)
        {
            // Observer installation is user code too: retain #20's reservation and
            // rollback/retry semantics, and never invoke it under the range lock.
            beforeStart?.Invoke(executor);
            receiverResource = ReceiverPreparation?.Invoke();
            executor.PrepareContext(nodes, values, owner, () => engine.ReleaseExecution(owner), Member, RollbackReceiver);
        }

        internal void Commit(FSMExecutor executor)
        {
            executor.CommitPreparedContext();
            started = true;
            Member.Executor = executor;
            Member.Round = executor.Execution;
        }

        internal void RollbackReceiver()
        {
            var resource = receiverResource;
            receiverResource = null;
            resource?.Dispose();
        }
    }
}
