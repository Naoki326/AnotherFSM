using System.Runtime.CompilerServices;
using AnotherFSM.Semantics;
using JetBrains.Application;
using JetBrains.Application.Parts;
using JetBrains.Application.Progress;
using JetBrains.Application.Threading;
using JetBrains.ReSharper.Psi.Impl.Search;
using JetBrains.ReSharper.Psi.Impl.Search.Operations;
using JetBrains.ReSharper.Psi.Search;

namespace AnotherFSM.Rider;

/// <summary>Keep Rider's visitor, scope intersection, scheduling and per-job duplicate checks;
/// aggregate only AnotherFSM physical positions at the operation's delivery boundary.</summary>
// Shell catalogs use PartSelector.LeafsAndHides (CatalogComponents.RegisterCatalog).
// A registered derived component replaces the base part and serves its base-type requests.
[ShellComponent(Instantiation.DemandAnyThreadSafe)]
public sealed class FsmFinderSearchDomainVisitorFactory(IShellLocks shellLocks) : FinderSearchDomainVisitorFactory(shellLocks)
{
    // FinderOperation creates one SynchronizedConsumer per operation, even if callers reuse their
    // collector. Named and additional phases pass that same consumer here. Never key on the
    // per-file/per-element DuplicateCheckConsumer created *inside* the SDK visitor.
    private readonly ConditionalWeakTable<object, QueryState> queries = new();

    public override IFinderSearchDomainVisitor Create<TResult>(ISearchDomain searchDomain,
        ICollection<ISearchProcessor> searchers, IFindResultConsumer<TResult> consumer,
        IProgressIndicator progressIndicator, IFinderSearchRoot root, bool isParallel)
        => base.Create(searchDomain, searchers,
            new EnvelopeConsumer<TResult>(consumer, queries.GetValue(consumer, _ => new QueryState())),
            progressIndicator, root, isParallel);

    // The consumer identifies the operation's complete target set, not a single host/TFM target.
    // Hosts already contains all matching query-target associations, computed by FsmSearcher.
    // Positions deliberately contain no PSI objects, module identities or deferred PSI access.
    private readonly record struct Position(Guid Session, long Epoch, string Document, SourceSpan Span);

    private sealed class QueryState
    {
        public readonly object Gate = new();
        public readonly Dictionary<Position, FindExecution> Delivered = new();
    }

    private sealed class Envelope<TResult>(TResult value, Position? position) : IEquatable<Envelope<TResult>>
    {
        public TResult Value { get; } = value;
        public Position? PhysicalPosition { get; } = position;

        // The SDK's HashSet<TResult> must still see precisely the caller's TResult equality.
        // In particular, a fresh wrapper must not make ordinary C# results unique, nor may a
        // physical-position marker coalesce distinct caller values before successful delivery.
        public bool Equals(Envelope<TResult>? other)
            => other != null && EqualityComparer<TResult>.Default.Equals(Value, other.Value);
        public override bool Equals(object? obj) => obj is Envelope<TResult> other && Equals(other);
        public override int GetHashCode() => Value is null ? 0 : EqualityComparer<TResult>.Default.GetHashCode(Value);
    }

    private sealed class EnvelopeConsumer<TResult>(IFindResultConsumer<TResult> inner, QueryState query)
        : IFindResultConsumer<Envelope<TResult>>
    {
        public Envelope<TResult>? Build(FindResult result)
        {
            // Build remains unsynchronized, exactly once per SDK call. Null means skip: neither
            // a successful Build nor the SDK's intermediate Merge reserves a physical position.
            var value = inner.Build(result);
            if (value is null) return null;
            Position? position = result is FsmKeyResult fsm
                ? new Position(fsm.Index.Session, fsm.Generation, fsm.DocumentId.ToUpperInvariant(), fsm.Reference.Span)
                : null;
            return new Envelope<TResult>(value, position);
        }

        public FindExecution Merge(Envelope<TResult> data)
        {
            if (data.PhysicalPosition is not { } position) return inner.Merge(data.Value);
            lock (query.Gate)
            {
                if (query.Delivered.TryGetValue(position, out var execution)) return execution;
                // Serialize check/deliver/record across parallel jobs and both phases. A throw
                // leaves this position retryable; Stop is a successful delivery and is retained.
                execution = inner.Merge(data.Value);
                query.Delivered.Add(position, execution);
                return execution;
            }
        }
    }
}
