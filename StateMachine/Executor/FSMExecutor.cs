using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Disposables;
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
        {
            this.start = start ?? throw new FSMException("Start 节点不能为空！");
            this.endEvent = endEvent ?? throw new FSMException("结束事件不能为空！");

            eventConsumer = Channel.CreateUnbounded<FSMEvent>();
            State = FSMState.Initialized;

            InitObserver(isAsyncObserver);

            eventAggregator = FSMEventAggregator.EventAggregator;
        }

        public FSMExecutor(IFSMNode start, FSMEvent endEvent) : this(start, endEvent, false)
        {
        }

        private readonly IEventAggregator eventAggregator;

        protected Channel<FSMEvent> eventConsumer;

        protected IFSMNode currentNode = default!;

        protected IFSMNode start;
        protected FSMEvent endEvent;

        protected FSMEvent pauseEvent = new("PauseEvent");
        protected FSMEvent PauseEvent => pauseEvent;

        protected FSMEvent continueEvent = new("ContinueEvent");
        protected FSMEvent ContinueEvent => continueEvent;

        public Task ExecutorTask { get; private set; } = default!;
        public Task CurrentNodeTask => currentNode.WaitCurrentTask;


        private FSMState state = FSMState.Uninitialized;
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

            using (Observable
                .FromEvent((v) => currentNode.RaisePause += v, (v) => currentNode.RaisePause -= v)
                .Subscribe(_ => Pause()))
            {
                currentNode.ExecuterContext = SolverContext;
                State = FSMState.Running;

                IDisposable? groupNodeHandle = default;
                if (currentNode is IObservable<StateTrackInfo> stateObs)
                {
                    groupNodeHandle = stateObs.Subscribe(stateTrackObservable);
                }
                try
                {
                    if (isCreateNew)
                    { await currentNode.CreateNewAsync(); }
                    isExit = await currentNode.RunAsync();
                }
                catch (Exception ex)
                {
                    stateTrackObservable.OnError(ex);
                    isExit = true;
                }
                finally
                {
                    groupNodeHandle?.Dispose();
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
            if (isLongRunning)
            {
                prevContext = SynchronizationContext.Current;
                executorContext = new FSMSyncContext(prevContext);
                SynchronizationContext.SetSynchronizationContext(executorContext);
                await Task.Yield();
            }

            long threadId = Thread.CurrentThread.ManagedThreadId;
            eventAggregator.Subscribe(this);
            try
            {
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
                        if (@event.EventID == ContinueEvent.EventID)
                        {
                            //这里是暂停之后继续的分支
                            TrackContinue(threadId);
                            isExit = await RunCurrentNodeAsync(false, threadId);
                            TrackContinueEnd(isExit, threadId);
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
                                    currentNode.Context.TriggerEvent = @event;
                                    IFSMNode nextNode = currentNode.TargetState(@event);
                                    nextNode.Context = currentNode.Context;
                                    SolverContext.LastNodeName = currentNode.Name;
                                    SolverContext.CurrentNodeName = nextNode.Name;

                                    TrackStateEnter(threadId, @event, nextNode);
                                    currentNode = nextNode;
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
                stateTrackObservable.OnError(ex);

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
                eventAggregator.Unsubscribe(this);
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
                if (!visited.Contains(targets))
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

        public void Stop()
        {
            TrackCallname();
            State = FSMState.Stopping;
            _ = Task.Run(async () =>
            {
                using IDisposable state2Stop = Disposable.Create(() =>
                {
                    State = FSMState.Stoped;
                });
                while (eventConsumer.Reader.Count > 0)
                { _ = eventConsumer.Reader.TryRead(out _); }
                Exception e = default!;
                if (currentNode != null && !currentNode.Context.IsPaused)
                {
                    currentNode.Context.Pause();
                    try
                    {
                        await currentNode.WaitCurrentTask;
                    }
                    catch (Exception ex)
                    {
                        e = ex;
                    }
                }
                while (eventConsumer.Reader.Count > 0)
                { _ = eventConsumer.Reader.TryRead(out _); }
                _ = eventConsumer.Writer.TryComplete();
                if (ExecutorTask != null)
                {
                    try
                    {
                        await ExecutorTask;
                    }
                    catch (Exception ex)
                    {
                        if (e != null)
                        {
                            throw new FSMException($"WaitCurrentTask 等待异常. {e.Message}");
                        }
                        throw new FSMException($"NodeTask 等待异常. {ex.Message}");
                    }
                }
            });
            return;
        }

        public async Task<bool> StopAsync()
        {
            TrackCallname();
            State = FSMState.Stopping;
            using IDisposable state2Stop = Disposable.Create(() =>
            {
                State = FSMState.Stoped;
            });
            if (eventConsumer is not null)
            {
                while (eventConsumer.Reader.Count > 0)
                { _ = eventConsumer.Reader.TryRead(out _); }
            }
            Exception e = default!;
            if (currentNode != null && !currentNode.Context.IsPaused)
            {
                currentNode.Context.Pause();
                try
                {
                    await currentNode.WaitCurrentTask;
                }
                catch (Exception ex)
                {
                    e = ex;
                }
            }
            while (eventConsumer.Reader.Count > 0)
            { _ = eventConsumer.Reader.TryRead(out _); }
            _ = eventConsumer.Writer.TryComplete();
            if (ExecutorTask != null)
            {
                try
                {
                    await ExecutorTask;
                }
                catch (Exception ex)
                {
                    if (e != null)
                    {
                        throw new FSMException($"WaitCurrentTask 等待异常. {e.Message}");
                    }
                    throw new FSMException($"NodeTask 等待异常. {ex.Message}");
                }
            }
            return true;
        }

        private async Task<bool> WaitStopAsync()
        {
            TrackCallname();
            State = FSMState.Stopping;
            using IDisposable state2Stop = Disposable.Create(() =>
            {
                State = FSMState.Stoped;
            });
            if (eventConsumer is not null)
            {
                while (eventConsumer.Reader.Count > 0)
                { _ = eventConsumer.Reader.TryRead(out _); }
            }
            Exception e = default!;
            if (currentNode != null && !currentNode.Context.IsPaused)
            {
                try
                {
                    await currentNode.WaitCurrentTask;
                }
                catch (Exception ex)
                {
                    e = ex;
                }
            }
            while (eventConsumer.Reader.Count > 0)
            { _ = eventConsumer.Reader.TryRead(out _); }
            _ = eventConsumer.Writer.TryComplete();
            if (ExecutorTask != null)
            {
                try
                {
                    await ExecutorTask;
                }
                catch (Exception ex)
                {
                    if (e != null)
                    {
                        throw new FSMException($"WaitCurrentTask 等待异常. {e.Message}");
                    }
                    throw new FSMException($"NodeTask 等待异常. {ex.Message}");
                }
            }
            return true;
        }

        private void InitNodes()
        {
            start.InitBeforeStart();
            foreach (IFSMNode child in start)
            {
                child.InitBeforeStart();
            }
        }


        public async Task<bool> RestartAsync(bool isLongRunning = false)
        {
            TrackCallname();
            if (!await WaitStopAsync())
            { return false; }
            if (eventConsumer != null && !eventConsumer.Reader.Completion.IsCompleted)
            { return false; }

            currentNode = start;

            pausing = false;
            start.Context ??= new FSMNodeContext();
            eventConsumer = Channel.CreateUnbounded<FSMEvent>();

            InitNodes();
            ExecutorTask = await Task.Factory.StartNew(() => ConsumerTask(isLongRunning));

            return true;
        }

        public async Task<bool> RestartAsync<T>(T data, bool isLongRunning = false) where T : class
        {
            start.Context = new FSMNodeContext<T>() { Data = data };
            return await RestartAsync(isLongRunning);
        }

        public async Task<bool> RestartAsync(IFSMNode node, bool isLongRunning = false)
        {
            TrackCallname();
            if (!await WaitStopAsync())
            { return false; }
            if (eventConsumer != null && !eventConsumer.Reader.Completion.IsCompleted)
            { return false; }

            currentNode = node ?? throw new FSMException("开始节点不能为空！");

            pausing = false;
            node.Context ??= new FSMNodeContext();
            eventConsumer = Channel.CreateUnbounded<FSMEvent>();

            InitNodes();
            ExecutorTask = await Task.Factory.StartNew(() => ConsumerTask(isLongRunning));

            return true;
        }

        public async Task<bool> RestartAsync<T>(IFSMNode node, T data, bool isLongRunning = false) where T : class
        {
            node.Context = new FSMNodeContext<T>() { Data = data };
            return await RestartAsync(node, isLongRunning);
        }

        private bool pausing = false;

        //暂停
        public void Pause()
        {
            TrackCallname();
            if (currentNode.Context.IsPaused || pausing || (State != FSMState.Running && State != FSMState.Proceeding))
            { return; }
            _ = Task.Run(async () =>
            {
                State = FSMState.Pausing;
                pausing = true;
                using IDisposable state2Pause = Disposable.Create(() =>
                {
                    pausing = false;
                    State = FSMState.Paused;
                });
                currentNode.Context.Pause();
                try
                {
                    await currentNode.WaitCurrentTask;
                }
                catch (Exception)
                {
                }
            });
            return;
        }

        public async Task<bool> PauseAsync()
        {
            TrackCallname();
            if (currentNode.Context.IsPaused || pausing || (State != FSMState.Running && State != FSMState.Proceeding))
            { return false; }

            State = FSMState.Pausing;
            pausing = true;
            using IDisposable state2Pause = Disposable.Create(() =>
            {
                pausing = false;
                State = FSMState.Paused;
            });
            currentNode.Context.Pause();
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
            if (currentNode.Context.IsPaused || pausing)
            {
                State = FSMState.Proceeding;
                _ = eventConsumer.Writer.TryWrite(ContinueEvent);
                State = FSMState.Running;
                return true;
            }

            return false;
        }

        [DebuggerHidden]
        public void Handle(FSMEvent @event)
        {
            if (@event.EventID == endEvent.EventID)
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

    public partial class FSMExecutor : IDisposable
    {
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                }
                // TODO: 释放未托管的资源(未托管的对象)并替代终结器
                if (eventConsumer != null)
                {
                    eventAggregator.Unsubscribe(this);
                    _ = eventConsumer.Writer.TryComplete();
                    stateTrackObservable.OnCompleted();
                    executeTrackObservable.OnCompleted();
                    eventLoopScheduler.Dispose();
                }
                if (ExecutorTask != null)
                {
                    try
                    {
                        ExecutorTask.Wait();
                    }
                    catch (Exception)
                    {
                    }
                    ExecutorTask.Dispose();
                    //executorContext.Dispose();
                }

                // TODO: 将大型字段设置为 null
                eventConsumer = default!;
                ExecutorTask = default!;
                //executorContext = default!;

                disposedValue = true;
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
