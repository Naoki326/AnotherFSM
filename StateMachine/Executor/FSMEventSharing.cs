namespace StateMachine
{
    /// <summary>A concrete prepared or started execution, never an engine-current alias.</summary>
    public interface IFSMEventSharingExecution { }

    /// <summary>Framework-owned business event sharing and receiver-first coordinated startup.</summary>
    public static class FSMEventSharing
    {
        internal static readonly object Sync = new object();

        internal sealed class Member
        {
            internal HashSet<Member> Group;
            internal FSMContextPreparation? Preparation;
            internal FSMExecutor? Executor;
            internal FSMExecution? Round;
            internal bool Exited;
            internal bool Starting;
            internal Member() { Group = new HashSet<Member> { this }; }
        }

        private static Member Resolve(IFSMEventSharingExecution execution)
            => execution switch
            {
                FSMContextPreparation preparation => preparation.Member,
                FSMExecution round => round.Member,
                _ => throw new FSMException("Connect requires concrete prepared or running executions; null/custom members are invalid.")
            };

        private static void Validate(Member member)
        {
            if (member.Exited || member.Executor != null && !member.Executor.AcceptsBusinessEvents)
                throw new FSMException("Sharing member is released, stopping or exited.");
            if (member.Group.Any(m => m.Starting))
                throw new FSMException("Sharing range is being started; retry after startup completes.");
        }

        public static void Connect(params IFSMEventSharingExecution[] executions)
        {
            if (executions == null || executions.Length == 0) throw new FSMException("Connect requires at least one execution.");
            var members = executions.Select(Resolve).Distinct().ToArray();
            lock (Sync)
            {
                foreach (var member in members) Validate(member);
                var merged = new HashSet<Member>(members.SelectMany(m => m.Group));
                foreach (var member in merged) member.Group = merged;
            }
        }

        public static Task<IReadOnlyList<FSMExecutor>> StartAsync(params FSMContextPreparation[] preparations)
            => Task.FromResult(Start(preparations, false));

        internal static IReadOnlyList<FSMExecutor> Start(FSMContextPreparation[] preparations, bool isLongRunning)
        {
            if (preparations == null || preparations.Length == 0 || preparations.Any(p => p == null))
                throw new FSMException("Start requires valid prepared executions.");
            var pending = preparations.ToArray();
            if (pending.Distinct().Count() != pending.Length) throw new FSMException("Start contains duplicate executions/engines.");
            FSMExecutor[] executors;
            lock (Sync)
            {
                foreach (var preparation in pending)
                {
                    Validate(preparation.Member);
                    preparation.ValidateStart();
                }
                var selected = new HashSet<Member>(pending.Select(p => p.Member));
                if (selected.Any(m => m.Group.Any(other => other.Executor == null && !other.Exited && !selected.Contains(other))))
                    throw new FSMException("Start must include every pending execution in each sharing range.");
                executors = pending.Select(p => p.CreateExecutor()).ToArray();
                foreach (var preparation in pending) preparation.Member.Starting = true;
            }
            try
            {
                // Establish every receiver before publishing membership or invoking any node.
                for (var i = 0; i < pending.Length; i++) pending[i].PrepareReceiver(executors[i], isLongRunning);
                lock (Sync)
                {
                    foreach (var preparation in pending) preparation.ValidateStart(reserved: true);
                    for (var i = 0; i < pending.Length; i++) pending[i].Commit(executors[i]);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var preparation in pending)
                {
                    try { preparation.RollbackReceiver(); }
                    catch (Exception cleanup) { errors.Add(cleanup); }
                }
                foreach (var executor in executors)
                {
                    try { executor.Dispose(); }
                    catch (Exception cleanup) { errors.Add(cleanup); }
                }
                lock (Sync) foreach (var preparation in pending) preparation.Member.Starting = false;
                throw new FSMException("Receiver preparation failed; no nodes were released. Valid preparations retain ownership for retry or disposal.",
                    errors.Count == 1 ? failure : new AggregateException(errors));
            }
            lock (Sync) foreach (var preparation in pending) preparation.Member.Starting = false;
            // User initialization remains outside the range lock and shares #19's exit barrier.
            Exception? error = null;
            for (var i = 0; i < pending.Length; i++)
            {
                try { executors[i].RunPreparedContext(isLongRunning); }
                catch (Exception ex) { error = error == null ? ex : new AggregateException(error, ex); }
            }
            if (error != null) throw error;
            return executors;
        }

        internal static void Exit(Member member)
        {
            lock (Sync)
            {
                member.Exited = true;
                member.Group.Remove(member);
                member.Group = new HashSet<Member>();
            }
        }

        internal static void Publish(Member source, FSMExecution round, FSMEvent value)
        {
            (FSMExecutor Executor, FSMExecution Round)[] targets;
            lock (Sync)
            {
                if (source.Exited || !ReferenceEquals(source.Round, round) || !source.Executor!.AcceptsBusinessEvents) return;
                // One direct fan-out per publication: no forwarding, no unbounded notification history.
                targets = source.Group.Where(m => !m.Exited && m.Executor != null && m.Round != null)
                    .Select(m => (m.Executor!, m.Round!)).ToArray();
            }
            foreach (var target in targets) target.Executor.ReceiveShared(target.Round, value, ReferenceEquals(target.Round, round));
        }
    }
}
