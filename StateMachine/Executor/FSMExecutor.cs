using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading.Channels;

namespace StateMachine
{

    public interface IFSMEventInterceptor
    {
        FSMEvent EventEntry(FSMEvent @event);
    }

    internal class FSMEventInterceptor : IFSMEventInterceptor
    {
        public FSMEvent EventEntry(FSMEvent @event)
        {
            return @event;
        }
    }

    [DebuggerNonUserCode]
    public partial class FSMExecutor : IHandle<FSMEvent>, IEnumerable<IFSMNode>
    {

        public FSMExecutor(IFSMNode start, FSMEvent endEvent, bool isAsyncObserver)
            : this(start, new[] { endEvent }, isAsyncObserver)
        {
        }

        public FSMExecutor(IFSMNode start, IEnumerable<FSMEvent> endEvents, bool isAsyncObserver)
        {
            this.start = start ?? throw new FSMException("Start 节点不能为空！");

            var distinctEndEvents = (endEvents ?? throw new FSMException("结束事件不能为空！"))
                .Where(e => e != null)
                .GroupBy(e => e.EventID)
                .Select(g => g.First())
                .ToList();
            if (distinctEndEvents.Count == 0)
                throw new FSMException("结束事件不能为空！");

            this.endEvent = distinctEndEvents[0];
            this.endEvents = distinctEndEvents;

            eventConsumer = Channel.CreateUnbounded<FSMEvent>();
            State = FSMState.Initialized;

            InitObserver(isAsyncObserver);

            engine = FSMEngine.OwnerOf(start) ?? new FSMEngine(null!);
            foreach (var node in this.Distinct(FSMNodeIdentityComparer.Instance)) engine.ClaimNode(node, node.Name);
        }

        public FSMExecutor(IFSMNode start, FSMEvent endEvent) : this(start, endEvent, false)
        {
        }

        public FSMExecutor(IFSMNode start, IEnumerable<FSMEvent> endEvents) : this(start, endEvents, false)
        {
        }

        private readonly FSMEngine engine;

        protected Channel<FSMEvent> eventConsumer;

        protected IFSMNode currentNode = default!;

        protected IFSMNode start;
        protected FSMEvent endEvent;
        protected IReadOnlyList<FSMEvent> endEvents;

        protected FSMEvent pauseEvent = new("PauseEvent");
        protected FSMEvent PauseEvent => pauseEvent;

        protected FSMEvent continueEvent = new("ContinueEvent");
        protected FSMEvent ContinueEvent => continueEvent;

        public Task ExecutorTask { get; private set; } = default!;
        public FSMExecution Execution { get; private set; } = default!;

        internal T ForExecution<T>(FSMExecution execution, Func<T> operation)
        {
            lock (controlSync)
            {
                if (!ReferenceEquals(Execution, execution) || execution.Completion.IsCompleted)
                    throw new FSMException($"Execution is stale or exited; cannot operate executor in state {State}.");
                return operation();
            }
        }

        internal bool AcceptsBusinessEvents => !disposedValue && ExecutorTask != null && !ExecutorTask.IsCompleted
            && State != FSMState.Stopping && State != FSMState.Stoped && State != FSMState.Finished;

        internal void PublishToExecution(FSMExecution round, string name, object? payload)
        {
            FSMEvent notification;
            lock (controlSync)
            {
                ForExecution(round, () => true);
                if (!AcceptsBusinessEvents) throw new FSMException($"Execution in state {State} cannot publish business event {name}.");
                var value = engine.TryGetEvent(name, out var local) ? local
                    : endEvents.FirstOrDefault(e => e.EventName == name)
                    ?? throw new FSMException($"Event {name} does not exist in this execution.");
                notification = (FSMEvent)value.Clone();
                notification.EventContext = payload;
            }
            FSMEventSharing.Publish(round.Member, round, notification);
        }

        internal void ReceiveShared(FSMExecution round, FSMEvent value)
        {
            lock (controlSync)
            {
                if (!ReferenceEquals(Execution, round) || !AcceptsBusinessEvents) return;
                // Event IDs are graph-local. Full ordinal names authorize both ordinary and end events.
                var local = engine.TryGetEvent(value.EventName, out var found) ? found
                    : endEvents.FirstOrDefault(e => string.Equals(e.EventName, value.EventName, StringComparison.Ordinal));
                if (local == null) return;
                var notification = (FSMEvent)local.Clone();
                notification.EventContext = value.EventContext;
                ReceiveEvent(notification);
            }
        }
        public Task CurrentNodeTask => currentNode.WaitCurrentTask;


        private volatile FSMState state = FSMState.Uninitialized;
        public FSMState State
        {
            get => state; private set
            {
                if (state == value)
                {
                    return;
                }

                FSMState prev_state = state;
                state = value;
                TrackFSMStateChanged(value, prev_state);
            }
        }

        internal IExecuterContext SolverContext { get; set; } = new ExecuterContext();

        public void PauseByAnchor(Enum eAnchor)
        {
            _ = SolverContext.PauseAnchors.Add(Convert.ToInt64(eAnchor));
        }

        public void PauseByAnchor(long lAnchor)
        {
            _ = SolverContext.PauseAnchors.Add(lAnchor);
        }

        public void PauseByAnchors(IEnumerable<Enum> eAnchors)
        {
            foreach (Enum eAnchor in eAnchors)
            { _ = SolverContext.PauseAnchors.Add(Convert.ToInt64(eAnchor)); }
        }

        public void PauseByAnchors(IEnumerable<long> lAnchors)
        {
            foreach (long lAnchor in lAnchors)
            { _ = SolverContext.PauseAnchors.Add(lAnchor); }
        }

        public void RemoveAncho(Enum eAnchor)
        {
            _ = SolverContext.PauseAnchors.Remove(Convert.ToInt64(eAnchor));
        }

        public void RemoveAnchor(long lAnchor)
        {
            _ = SolverContext.PauseAnchors.Remove(lAnchor);
        }

        public void RemoveAnchors(IEnumerable<Enum> eAnchors)
        {
            foreach (Enum eAnchor in eAnchors)
            { _ = SolverContext.PauseAnchors.Remove(Convert.ToInt64(eAnchor)); }
        }

        public void RemoveAnchors(IEnumerable<long> lAnchors)
        {
            foreach (long lAnchor in lAnchors)
            { _ = SolverContext.PauseAnchors.Remove(lAnchor); }
        }

        public void ResetAnchors()
        {
            SolverContext.PauseAnchors.Clear();
        }

        // 该接口可以改变传入的事件，可以在界面上暂停
        public IFSMEventInterceptor EventInterceptor { get; set; } = new FSMEventInterceptor();

        private async Task<bool> RunCurrentNodeAsync(bool isCreateNew, long threadId)
        {
            bool isExit = false;
            if (currentNode is null)
            {
                throw new FSMException("CurrentNode is null");
            }

            var round = Execution;
            var invocation = new object();
            using (FSMNodeEventScope.Enter(engine, invocation, value =>
            {
                FSMEventSharing.Publish(round.Member, round, value);
            }, () =>
            {
                lock (controlSync)
                    if (ReferenceEquals(Execution, round) && !round.Completion.IsCompleted) Pause(nodeRequested: true);
            }))
            using (Observable
                .FromEvent((v) => currentNode.RaisePause += v, (v) => currentNode.RaisePause -= v)
                .Subscribe(_ =>
                {
                    lock (controlSync)
                        if (ReferenceEquals(Execution, round) && !round.Completion.IsCompleted) Pause(nodeRequested: true);
                }))
            {
                currentNode.ExecuterContext = SolverContext;

                try
                {
                    lock (controlSync)
                    {
                        if (State == FSMState.Stopping || State == FSMState.Stoped || State == FSMState.Finished) return false;
                        (currentNode as AbstractFSMNode)?.BeginInvocation(invocation, isCreateNew);
                    }
                    if (isCreateNew)
                    { await currentNode.CreateNewAsync(); }
                    while (true)
                    {
                        await WaitForResumeAsync();
                        lock (controlSync)
                        {
                            if (State == FSMState.Stopping || State == FSMState.Stoped || State == FSMState.Finished) return false;
                            if (State == FSMState.Pausing || State == FSMState.Paused) continue;
                            // A pause at the enter notification precedes this node's first
                            // invocation; continuing starts it once, not a second resume.
                            if (isCreateNew && State == FSMState.Proceeding) Interlocked.Exchange(ref resumePending, 0);
                            (currentNode as AbstractFSMNode)?.PrepareRun(invocation);
                            break;
                        }
                    }
                    // Do not hold the control lock across user code: synchronous,
                    // cooperative work must still be able to observe a concurrent Stop.
                    var running = currentNode.RunAsync();
                    lock (controlSync)
                    {
                        if (State == FSMState.Initialized || State == FSMState.Proceeding)
                            State = FSMState.Running;
                        if (Interlocked.Exchange(ref pendingPause, 0) != 0)
                            Pause(nodeRequested: true);
                    }
                    isExit = await running;
                }
                catch (Exception ex)
                {
                    isExit = true;
                }
                if (currentNode.Context.IsPaused)
                {
                    pausing = false;
                }
            }

            return isExit;
        }

        private async Task ConsumerTask(bool isLongRunning)
        {
            // 设定同步上下文
            SynchronizationContext? prevContext = null;
            FSMSyncContext executorContext = null!;
            isLongRunning = isLongRunning && !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Create("BROWSER"));
            if (isLongRunning)
            {
                prevContext = SynchronizationContext.Current;
                executorContext = new FSMSyncContext(prevContext);
                SynchronizationContext.SetSynchronizationContext(executorContext);
                await Task.Yield();
            }

            // Yield to the host event loop, including single-thread browser WASM. A
            // timer also prevents a graph of synchronous nodes starving UI controls.
            long threadId = 0;
            try
            {
                await Task.Delay(1);
                if (State == FSMState.Stopping || State == FSMState.Stoped) return;
                threadId = Thread.CurrentThread.ManagedThreadId;
                bool isExit = false;

                //这里是第一个启动节点
                SolverContext.CurrentNodeName = start.Name;
                TrackStart(threadId);
                isExit = await RunCurrentNodeAsync(true, threadId);
                TrackStartEnd(isExit, threadId);

                while (await eventConsumer.Reader.WaitToReadAsync())
                {
                    while (eventConsumer.Reader.TryRead(out FSMEvent? @event))
                    {
                        await Task.Delay(1);
                        if (State == FSMState.Stopping || State == FSMState.Stoped) return;
                        await WaitForResumeAsync();
                        if (State == FSMState.Stopping || State == FSMState.Stoped) return;
                        if (Interlocked.Exchange(ref resumePending, 0) != 0)
                        {
                            //这里是暂停之后继续的分支
                            TrackContinue(threadId);
                            isExit = await RunCurrentNodeAsync(false, threadId);
                            TrackContinueEnd(isExit, threadId);
                            await WaitForResumeAsync();
                        }
                        if (State == FSMState.Stopping || State == FSMState.Stoped) return;
                        if (@event.EventID == ContinueEvent.EventID) continue;
                        if (endEvents.Any(e => e.EventID == @event.EventID))
                        {
                            lock (controlSync)
                            {
                                if (State == FSMState.Stopping || State == FSMState.Stoped) return;
                                State = FSMState.Finished;
                                eventConsumer.Writer.TryComplete();
                            }
                            return;
                        }
                        else
                        {
                            if (currentNode.HasTransition(@event))
                            {
                                // 如果可能触发当前状态机变换状态

                                // 其他应用或界面可通过EventInterceptor接口获取事件并改变事件
                                @event = EventInterceptor.EventEntry(@event);
                                // 再次检查新的event能否触发状态变换
                                if (currentNode.HasTransition(@event))
                                {
                                    //这里是正常执行节点的分支
                                    while (true)
                                    {
                                        await WaitForResumeAsync();
                                        lock (controlSync)
                                        {
                                            if (State == FSMState.Stopping || State == FSMState.Stoped || State == FSMState.Finished) return;
                                            if (State == FSMState.Pausing || State == FSMState.Paused) continue;
                                            IFSMNode nextNode = currentNode.TargetState(@event);
                                            if (!hasBoundContext)
                                                nextNode.Context = currentNode.Context;
                                            nextNode.Context.TriggerEvent = @event;
                                            SolverContext.LastNodeName = currentNode.Name;
                                            SolverContext.CurrentNodeName = nextNode.Name;

                                            TrackStateEnter(threadId, @event, nextNode);
                                            currentNode = nextNode;
                                            break;
                                        }
                                    }
                                    isExit = await RunCurrentNodeAsync(true, threadId);
                                    TrackStateExit(threadId, isExit);
                                }
                                else
                                {
                                    // 对当前的状态机无用的Event
                                    TrackNoUseEvent(threadId, @event);
                                }
                            }
                            else
                            {
                                // 对当前的状态机无用的Event
                                TrackNoUseEvent(threadId, @event);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //Track Exit
                TrackFSMExit(threadId);

                //这里位于Task中，若流程出现异常，Task自动退出
                //注意遇到任何异常，都需要检查IObservable的OnError或者NodeExceptionEvent事件
                return;
            }
            finally
            {
                if (isLongRunning)
                {
                    SynchronizationContext.SetSynchronizationContext(prevContext);
                }
                executorContext?.Dispose();
            }
        }

        private IEnumerable<IFSMNode> Traversal(IFSMNode firstNode, ConcurrentBag<IFSMNode> visited)
        {
            if (firstNode is null)
            {
                yield break;
            }

            foreach (IFSMNode targets in firstNode.GetAllTargets())
            {
                if (!visited.Any(node => ReferenceEquals(node, targets)))
                {
                    visited.Add(targets);
                    yield return targets;
                    foreach (IFSMNode t in Traversal(targets, visited))
                    {
                        yield return t;
                    }
                }
            }
        }

        public IEnumerator<IFSMNode> GetEnumerator()
        {
            yield return start;
            foreach (IFSMNode t in Traversal(start, []))
            {
                yield return t;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private readonly object controlSync = new object();
        public Exception? StopException { get; private set; }

        private TaskCompletionSource<bool>? stopCallbacks;

        public void Stop() => RequestStop(null);
        internal void Stop(FSMExecution execution) => RequestStop(execution);

        private Task? RequestStop(FSMExecution? execution)
        {
            FSMNodeContext? context;
            TaskCompletionSource<bool> callbacks;
            Task task;
            lock (controlSync)
            {
                if (execution != null && (!ReferenceEquals(Execution, execution) || execution.Completion.IsCompleted))
                    throw new FSMException($"Execution is stale or exited; cannot stop executor in state {State}.");
                task = ExecutorTask;
                if (task == null || task.IsCompleted) return task;
                if (State == FSMState.Stopping || State == FSMState.Stoped) return task;
                // Cancel can inline the entire node/consumer exit on this thread.
                // Publish its barrier before any notification can reenter exit.
                stopCallbacks = callbacks = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                context = currentNode?.Context;
                State = FSMState.Stopping;
                Interlocked.Exchange(ref pendingPause, 0);
                eventConsumer.Writer.TryComplete();
                resumeSignal?.TrySetResult(true);
            }
            // Cancellation callbacks are user code, not part of the control lock.
            try { context?.Pause(); }
            catch (Exception ex) { lock (controlSync) StopException = ex; }
            finally { callbacks.TrySetResult(true); }
            return task;
        }

        public Task<bool> StopAsync() => StopAsync(null);
        internal async Task<bool> StopAsync(FSMExecution? execution)
        {
            var task = RequestStop(execution);
            if (task != null) await task;
            return true;
        }

        private bool hasBoundContext;
        private IFSMNode[] executionNodes = Array.Empty<IFSMNode>();

        internal void StartContext(IFSMNode[] nodes, IReadOnlyList<(IFSMNode Node, object Value)> values, bool isLongRunning, object owner, Action release)
        {
            hasBoundContext = true;
            executionNodes = nodes;
            foreach (var node in nodes) node.Context = new FSMNodeContext();
            foreach (var binding in values) ((IFSMContextBindingTarget)binding.Node).BindContext(binding.Value);
            currentNode = start;
            eventConsumer = Channel.CreateUnbounded<FSMEvent>();
            StartOwned(isLongRunning, owner, release);
        }

        private object preparedOwner = null!;
        private Action preparedRelease = null!;
        private FSMEventSharing.Member? preparedMember;

        internal void PrepareContext(IFSMNode[] nodes, IReadOnlyList<(IFSMNode Node, object Value)> values,
            object owner, Action release, FSMEventSharing.Member member)
        {
            hasBoundContext = true;
            executionNodes = nodes;
            foreach (var node in nodes) node.Context = new FSMNodeContext();
            foreach (var binding in values) ((IFSMContextBindingTarget)binding.Node).BindContext(binding.Value);
            currentNode = start;
            eventConsumer = Channel.CreateUnbounded<FSMEvent>();
            preparedOwner = owner;
            preparedRelease = release;
            preparedMember = member;
        }

        internal void CommitPreparedContext() => BeginOwned(preparedOwner);

        private void BeginOwned(object owner)
        {
            FSMExecution round;
            lock (controlSync)
            {
                StopException = null;
                Interlocked.Exchange(ref resumePending, 0);
                resumeSignal = null;
                stopCallbacks = null;
                Execution = round = new FSMExecution(this, preparedMember ?? new FSMEventSharing.Member());
                round.Member.Executor = this;
                round.Member.Round = round;
                ExecutorTask = round.Completion;
                engine.DescribeExecution(owner, () => round.State.ToString());
                State = FSMState.Initialized;
            }
        }

        private void StartOwned(bool isLongRunning, object owner, Action release)
        {
            BeginOwned(owner);
            RunPreparedContext(isLongRunning, release);
        }

        internal void RunPreparedContext(bool isLongRunning, Action? release = null)
        {
            var round = Execution;
            release = release ?? preparedRelease;
            if (disposedValue) Stop();
            // Publish this round's exit barrier before entering synchronous user
            // initialization. Stop/Dispose during Init must survive node release.
            try { InitNodes(); }
            catch (Exception ex)
            {
                lock (controlSync) State = FSMState.Stopping;
                _ = RunOwnedAsync(round, isLongRunning, release, ex);
                throw;
            }
            _ = RunOwnedAsync(round, isLongRunning, release);
        }

        private async Task RunOwnedAsync(FSMExecution execution, bool isLongRunning, Action release, Exception? initializationError = null)
        {
            Exception? error = initializationError;
            try { if (initializationError == null) await ConsumerTask(isLongRunning); }
            catch (Exception ex) { error = ex; }
            finally
            {
                Task? callbacks;
                lock (controlSync) callbacks = stopCallbacks?.Task;
                if (callbacks != null) await callbacks;
                foreach (var node in executionNodes.OfType<AbstractFSMNode>())
                {
                    try { await node.CleanupExecutionAsync(); }
                    catch (Exception ex) { error = error == null ? ex : new AggregateException(error, ex); }
                }
                while (true)
                {
                    lock (controlSync)
                    {
                        callbacks = stopCallbacks?.Task;
                        if (callbacks == null || callbacks.IsCompleted)
                        {
                            lock (engine.ExecutionSync)
                            {
                                eventConsumer.Writer.TryComplete();
                                while (eventConsumer.Reader.TryRead(out _)) { }
                                if (State == FSMState.Stopping) State = FSMState.Stoped;
                                try { if (disposedValue) CompleteObservers(); }
                                catch (Exception ex) { error = error ?? ex; }
                                FSMEventSharing.Exit(execution.Member);
                                execution.Complete(State, error ?? StopException);
                                release();
                            }
                            break;
                        }
                    }
                    // A Stop racing cleanup must also finish collecting callback
                    // errors before completion and engine ownership are released.
                    await callbacks;
                }
            }
        }

        private void InitNodes()
        {
            foreach (var node in executionNodes) node.InitBeforeStart();
        }


        private void RejectBoundRestart(IFSMNode next)
        {
            if (hasBoundContext || engine.Concat(this).Concat(new[] { next }).Concat(Traversal(next, new ConcurrentBag<IFSMNode>()))
                .Any(n => n is IFSMContextNode contextual && contextual.ContextType != null))
                throw new FSMException("Typed execution requires a fresh flow.Prepare(input); legacy RestartAsync/Data cannot supply, reuse or replace its bindings.");
        }

        public Task<bool> RestartAsync(bool isLongRunning = false)
            => RestartLegacyAsync(start, () => new FSMNodeContext(), isLongRunning);

        public Task<bool> RestartAsync<T>(T data, bool isLongRunning = false) where T : class
            => RestartLegacyAsync(start, () => new FSMNodeContext<T> { Data = data }, isLongRunning);

        public Task<bool> RestartAsync(IFSMNode node, bool isLongRunning = false)
            => RestartLegacyAsync(node, () => new FSMNodeContext(), isLongRunning);

        public Task<bool> RestartAsync<T>(IFSMNode node, T data, bool isLongRunning = false) where T : class
            => RestartLegacyAsync(node, () => new FSMNodeContext<T> { Data = data }, isLongRunning);

        private async Task<bool> RestartLegacyAsync(IFSMNode node, Func<FSMNodeContext> contextFactory, bool isLongRunning)
        {
            if (node == null) throw new FSMException("开始节点不能为空！");
            RejectBoundRestart(node);
            if (disposedValue) throw new ObjectDisposedException(nameof(FSMExecutor));
            var nodes = engine.Concat(new[] { node }).Concat(Traversal(node, new ConcurrentBag<IFSMNode>()))
                .Distinct(FSMNodeIdentityComparer.Instance).ToArray();
            foreach (var candidate in nodes) engine.ClaimNode(candidate, candidate.Name);
            await StopAsync();
            var owner = new object();
            engine.AcquireExecution(owner);
            var owned = false;
            try
            {
                engine.ClaimNode(node, node.Name);
                start = node;
                currentNode = node;
                Interlocked.Exchange(ref pendingPause, 0);
                pausing = false;
                executionNodes = nodes;
                foreach (var child in executionNodes) child.Context = new FSMNodeContext();
                node.Context = contextFactory();
                eventConsumer = Channel.CreateUnbounded<FSMEvent>();
                owned = true;
                StartOwned(isLongRunning, owner, () => engine.ReleaseExecution(owner));
                return true;
            }
            catch { if (!owned) engine.ReleaseExecution(owner); throw; }
        }

        private TaskCompletionSource<bool>? resumeSignal;
        private Task WaitForResumeAsync()
        {
            lock (controlSync)
                return State == FSMState.Paused || State == FSMState.Pausing
                    ? resumeSignal!.Task : Task.CompletedTask;
        }

        private bool pausing = false;
        private int pendingPause;
        private int resumePending;

        //暂停，不等待返回
        public void Pause()
        {
            lock (controlSync)
            {
                if (ExecutorTask == null || ExecutorTask.IsCompleted)
                    throw new FSMException($"Cannot pause an unstarted or exited execution in state {State}.");
                Pause(nodeRequested: false);
            }
        }

        private void Pause(bool nodeRequested)
        {
            TrackCallname();
            if (!nodeRequested && State == FSMState.Initialized)
            {
                // An external caller can pause after the enter notification but
                // before RunAsync installs its token. Canceling the old token loses
                // that request, so deliver it after the synchronous RunAsync prefix.
                Interlocked.Exchange(ref pendingPause, 1);
                if (State == FSMState.Initialized) return;
                // Startup may have crossed into Running while we queued the request.
                // Either this caller or RunCurrentNodeAsync must deliver it, not both.
                if (Interlocked.Exchange(ref pendingPause, 0) == 0) return;
            }
            // An initial node can raise Pause synchronously inside RunAsync, after
            // its token is installed but before Running is announced externally.
            if (currentNode.Context.IsPaused || pausing || (State != FSMState.Running && State != FSMState.Proceeding
                && !(nodeRequested && State == FSMState.Initialized)))
            { return; }
            lock (controlSync)
            {
                if (State == FSMState.Stopping || State == FSMState.Stoped || State == FSMState.Finished) return;
                resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                State = FSMState.Pausing;
                pausing = true;
                try { currentNode.Context.Pause(); }
                finally
                {
                    pausing = false;
                    if (State == FSMState.Pausing) State = FSMState.Paused;
                }
            }
        }

        public async Task<bool> PauseAsync()
        {
            TrackCallname();
            if (ExecutorTask == null || ExecutorTask.IsCompleted || currentNode.Context.IsPaused || pausing
                || (State != FSMState.Running && State != FSMState.Proceeding))
            { return false; }

            Pause();
            try
            {
                await currentNode.WaitCurrentTask;
            }
            catch { }
            return true;
        }

        //继续
        public bool Continue()
        {
            TrackCallname();
            lock (controlSync)
            {
                if (State != FSMState.Paused || ExecutorTask == null || ExecutorTask.IsCompleted) return false;
                if (!eventConsumer.Writer.TryWrite(ContinueEvent)) return false;
                Interlocked.Exchange(ref resumePending, 1);
                State = FSMState.Proceeding;
                resumeSignal?.TrySetResult(true);
                return true;
            }
        }

        [DebuggerHidden]
        public void Handle(FSMEvent @event)
            => throw new FSMException("Execution identity is required; publish through the concrete FSMExecution handle.");

        // Only round-validated external calls and captured node routes reach here.
        private void ReceiveEvent(FSMEvent @event)
        {
            lock (controlSync)
            {
                if (ExecutorTask == null || ExecutorTask.IsCompleted || State == FSMState.Stopping || State == FSMState.Stoped || State == FSMState.Finished)
                    throw new FSMException($"Execution in state {State} cannot accept an external business event.");
                if ((State == FSMState.Paused || State == FSMState.Pausing)
                    && @event.EventID != PauseEvent.EventID)
                {
                    eventConsumer.Writer.TryWrite(@event);
                    return;
                }
                if (endEvents.Any(endEvent => @event.EventID == endEvent.EventID))
                {
                    _ = eventConsumer.Writer.TryComplete();
                    State = FSMState.Finished;
                }
                else if (@event.EventID == PauseEvent.EventID)
                {
                    Pause();
                }
                else if (!eventConsumer.Reader.Completion.IsCompleted)
                {
                    // 通过接口改变传入的事件
                    _ = eventConsumer.Writer.TryWrite(@event);
                }
            }
        }
    }

    public partial class FSMExecutor : IDisposable
    {
        private bool disposedValue;

        private void CompleteObservers()
        {
            stateTrackObservable.OnCompleted();
            executeTrackObservable.OnCompleted();
            eventLoopScheduler.Dispose();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            lock (controlSync)
            {
                if (disposedValue) return;
                disposedValue = true;
            }
            Stop();
            lock (controlSync)
            {
                // Active-round observer cleanup belongs inside the completion barrier.
                if (ExecutorTask == null || ExecutorTask.IsCompleted) CompleteObservers();
            }
        }

        // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        ~FSMExecutor()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
