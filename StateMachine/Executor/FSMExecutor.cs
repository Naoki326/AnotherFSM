using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace StateMachine
{

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

        private IEventAggregator eventAggregator;

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
                    return;
                var prev_state = state;
                state = value;
                FSMStateChangedInvoke(value, prev_state);
            }
        }

        private ExcecuterContext SolverContext { get; set; } = new ExcecuterContext();

        public long ManualLevel { get; set; } = 0;
        public Enum ManualELevel
        {
            set { ManualLevel = Convert.ToInt64(value); }
        }

        private async Task<bool> RunCurrentNodeAsync(bool isCreateNew)
        {
            bool isExit = false;
            if (currentNode is null)
                throw new FSMException("CurrentNode is null");
            //NodeStateChangedInvoke(currentNode.Name);
            currentNode.RaisePause += CurrentNode_RaisePause;
            currentNode.ExecuterContext = SolverContext;
            try
            {
                State = FSMState.Running;
                if (isCreateNew)
                { await currentNode.CreateNewAsync(); }
                currentNode.Context.ManualLevel = ManualLevel;
                isExit = await currentNode.RunAsync();
                //if (isExit)
                //{ NodeExitChangedInvoke(currentNode.Name); }
                if (currentNode.Context.IsPaused)
                {
                    while (eventConsumer.Reader.Count > 0)
                    { midEventList.Enqueue(await eventConsumer.Reader.ReadAsync()); }
                    pausing = false;
                }
            }
            finally
            {
                currentNode.RaisePause -= CurrentNode_RaisePause;
            }

            return isExit;
        }

        private async Task ConsumerTask(bool isLongRunning)
        {
            FSMSyncContext executorContext = null!;
            if (isLongRunning)
            {
                executorContext = new FSMSyncContext();
                SynchronizationContext.SetSynchronizationContext(executorContext);
                await Task.Yield();
            }
            long threadId = Thread.CurrentThread.ManagedThreadId;
            eventAggregator.Subscribe(this);
            try
            {
                bool isExit = false;

                //这里是第一个启动节点

                TrackStart(threadId);
                isExit = await RunCurrentNodeAsync(true);
                TrackStartEnd(isExit, threadId);

                while (await eventConsumer.Reader.WaitToReadAsync())
                {
                    while (eventConsumer.Reader.TryRead(out FSMEvent? @event))
                    {
                        if (@event.EventID == ContinueEvent.EventID)
                        {
                            //这里是暂停之后继续的分支
                            TrackContinue(threadId);
                            isExit = await RunCurrentNodeAsync(false);
                            TrackContinueEnd(isExit, threadId);
                        }
                        else if (currentNode.HasTransition(@event))
                        {
                            //这里是正常执行节点的分支
                            currentNode.Context.TriggerEvent = @event;
                            var nextNode = currentNode.TargetState(@event);
                            nextNode.Context = currentNode.Context;
                            SolverContext.LastNodeName = currentNode.Name;
                            SolverContext.CurrentNodeName = nextNode.Name;

                            TrackStateEnter(threadId, @event, nextNode);
                            currentNode = nextNode;
                            isExit = await RunCurrentNodeAsync(true);
                            TrackStateExit(threadId, isExit);
                        }
                        else
                        {
                            //无用的Event
                            TrackNoUseEvent(threadId, @event);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //Track Exit
                TrackFSMExit(threadId);
                observable.OnError(ex);

                //这里位于Task中，若流程出现异常，Task自动退出
                //注意遇到任何异常，都需要检查IObservable的OnError或者NodeExceptionEvent事件
                return;
            }
            finally
            {
                eventAggregator.Unsubscribe(this);
                executorContext?.Dispose();
            }
        }

        private void CurrentNode_RaisePause()
        {
            // 向当前的所有FSMExecutor发布暂停事件
            //eventAggregator.Publish(PauseEvent);
            Pause();
        }

        private IEnumerable<IFSMNode> Traversal(IFSMNode firstNode, ConcurrentBag<IFSMNode> visited)
        {
            if (firstNode is null)
                yield break;
            foreach (var targets in firstNode.GetAllTargets())
            {
                if (!visited.Contains(targets))
                {
                    visited.Add(targets);
                    yield return targets;
                    foreach (var t in Traversal(targets, visited))
                        yield return t;
                }
            }
        }

        public IEnumerator<IFSMNode> GetEnumerator()
        {
            yield return start;
            foreach (var t in Traversal(start, new ConcurrentBag<IFSMNode>()))
                yield return t;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Stop()
        {
            TrackCallname();
            State = FSMState.Stopping;
            while (eventConsumer.Reader.Count > 0)
            { eventConsumer.Reader.TryRead(out _); }
            Exception e = default!;
            Task.Run(async () =>
            {
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
                { eventConsumer.Reader.TryRead(out _); }
                eventConsumer.Writer.TryComplete();
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
                State = FSMState.Stoped;
            });
            return;
        }

        public async Task<bool> StopAsync()
        {
            TrackCallname();
            State = FSMState.Stopping;
            while (eventConsumer.Reader.Count > 0)
            { eventConsumer.Reader.TryRead(out _); }
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
            { eventConsumer.Reader.TryRead(out _); }
            eventConsumer.Writer.TryComplete();
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
            State = FSMState.Stoped;
            return true;
        }

        private void InitNodes()
        {
            start.InitBeforeStart();
            foreach (var child in start)
            {
                child.InitBeforeStart();
            }
        }


        public async Task<bool> RestartAsync(bool isLongRunning = false)
        {
            TrackCallname();
            if (!await StopAsync())
            { return false; }
            if (eventConsumer != null && !eventConsumer.Reader.Completion.IsCompleted)
            { return false; }

            currentNode = start;

            pausing = false;
            if (start.Context == null)
            { start.Context = new FSMNodeContext(); }
            while (midEventList.TryDequeue(out _)) { }
            eventConsumer = Channel.CreateUnbounded<FSMEvent>();

            InitNodes();
            ExecutorTask = await Task.Factory.StartNew(() => ConsumerTask(isLongRunning));

            return true;
        }

        public async Task<bool> RestartAsync(FSMNodeContext context, bool isLongRunning = false)
        {
            if (!await StopAsync())
            { return false; }
            start.Context = context;
            return await RestartAsync(isLongRunning);
        }

        public async Task<bool> RestartAsync(IFSMNode node, bool isLongRunning = false)
        {
            TrackCallname();
            if (!await StopAsync())
            { return false; }
            if (eventConsumer != null && !eventConsumer.Reader.Completion.IsCompleted)
            { return false; }

            currentNode = node ?? throw new FSMException("开始节点不能为空！");

            pausing = false;
            if (node.Context == null)
            { node.Context = new FSMNodeContext(); }
            while (midEventList.TryDequeue(out _)) { }
            eventConsumer = Channel.CreateUnbounded<FSMEvent>();

            InitNodes();
            ExecutorTask = await Task.Factory.StartNew(() => ConsumerTask(isLongRunning));

            return true;
        }

        public async Task<bool> RestartAsync(IFSMNode node, FSMNodeContext context, bool isLongRunning = false)
        {
            if (!await StopAsync())
            { return false; }
            node.Context = context;
            return await RestartAsync(node, isLongRunning);
        }

        private ConcurrentQueue<FSMEvent> midEventList = new ConcurrentQueue<FSMEvent>();
        private bool pausing = false;

        //暂停
        public void Pause()
        {
            TrackCallname();
            if (currentNode.Context.IsPaused || pausing || (State != FSMState.Running && State != FSMState.Proceeding))
            { return; }

            State = FSMState.Pausing;
            pausing = true;
            while (eventConsumer.Reader.Count > 0)
            {
                if (eventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                {
                    midEventList.Enqueue(fSMEvent);
                }
            }
            currentNode.Context.Pause();
            Task.Run(async () =>
            {
                try
                {
                    await currentNode.WaitCurrentTask;
                }
                finally
                {
                    while (eventConsumer.Reader.Count > 0)
                    {
                        if (eventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                        {
                            midEventList.Enqueue(fSMEvent);
                        }
                    }
                    pausing = false;
                    State = FSMState.Paused;
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
            while (eventConsumer.Reader.Count > 0)
            {
                if (eventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                {
                    midEventList.Enqueue(fSMEvent);
                }
            }
            currentNode.Context.Pause();
            try
            {
                await currentNode.WaitCurrentTask;
            }
            finally
            {
                while (eventConsumer.Reader.Count > 0)
                {
                    if (eventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                    {
                        midEventList.Enqueue(fSMEvent);
                    }
                }
                pausing = false;
                State = FSMState.Paused;
            }
            return true;
        }

        //继续
        public bool Continue()
        {
            TrackCallname();
            if (currentNode.Context.IsPaused || pausing)
            {
                State = FSMState.Proceeding;
                eventConsumer.Writer.TryWrite(ContinueEvent);
                while (midEventList.TryDequeue(out FSMEvent? e))
                { eventConsumer.Writer.TryWrite(e); }
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
                eventConsumer.Writer.TryComplete();
                State = FSMState.Finished;
            }
            else if (@event.EventID == PauseEvent.EventID)
            {
                Pause();
            }
            else if (currentNode.Context.IsPaused)
            {
                midEventList.Enqueue(@event);
            }
            else if (!eventConsumer.Reader.Completion.IsCompleted)
            {
                eventConsumer.Writer.TryWrite(@event);
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
                    eventConsumer.Writer.TryComplete();
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
