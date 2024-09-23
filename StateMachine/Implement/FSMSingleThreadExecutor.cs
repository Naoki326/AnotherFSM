using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace StateMachine
{
    public partial class FSMSingleThreadExecutor : IHandle<FSMEvent>, IEnumerable<IFSMNode>
    {

        public FSMSingleThreadExecutor(IFSMNode start, FSMEvent endEvent)
        {
            Start = start ?? throw new FSMException("Start 节点不能为空！");
            EndEvent = endEvent ?? throw new FSMException("结束事件不能为空！");

            EventConsumer = Channel.CreateUnbounded<FSMEvent>();
            State = FSMNodeState.Initialized;

            observer = new FSMNodeObserver(this);
            observerDisposeObj = (this as IObservable<string>).Subscribe(observer);

            eventAggregator = FSMEventAggregator.EventAggregator;
        }
        private IDisposable observerDisposeObj;

        private IEventAggregator eventAggregator;

        protected Channel<FSMEvent> EventConsumer;

        protected IFSMNode CurrentNode = default!;

        protected IFSMNode Start;
        protected FSMEvent EndEvent;

        protected FSMEvent pauseEvent = new("PauseEvent");
        protected FSMEvent PauseEvent => pauseEvent;

        protected FSMEvent continueEvent = new("ContinueEvent");
        protected FSMEvent ContinueEvent => continueEvent;

        private FSMEvent interuptEvent = new("InteruptEvent");
        public FSMEvent InteruptEvent => interuptEvent;

        public Task ExecutorTask { get; private set; } = default!;
        public Task CurrentNodeTask => CurrentNode.WaitCurrentTask;


        private FSMNodeState state = FSMNodeState.Uninitialized;
        public FSMNodeState State
        {
            get => state; private set
            {
                if (state == value)
                    return;
                var prev_state = state;
                state = value;
                FSMStateChanged?.Invoke(this, value, prev_state);
            }
        }

        public event EventHandler<string>? NodeStateChanged;
        public event EventHandler<string>? NodeExitChanged;
        //事件的参数：solver实例，新状态，前一状态
        public event Action<FSMSingleThreadExecutor, FSMNodeState, FSMNodeState>? FSMStateChanged;

        private ExcecuterContext SolverContext { get; set; } = new ExcecuterContext();

        public long ManualLevel { get; set; } = 0;
        public Enum ManualELevel
        {
            set { ManualLevel = Convert.ToInt64(value); }
        }

        private async Task<bool> RunCurrentNodeAsync(bool isCreateNew)
        {
            bool isCancel = false;
            if (CurrentNode is null)
                throw new FSMException("CurrentNode is null");
            NodeStateChanged?.Invoke(this, CurrentNode.Name);
            CurrentNode.RaiseInterrupt += CurrentNode_RaiseInterrupt;
            CurrentNode.RaisePause += CurrentNode_RaisePause;
            CurrentNode.ExecuterContext = SolverContext;
            try
            {
                State = FSMNodeState.Running;
                if (isCreateNew)
                { await CurrentNode.CreateNewAsync(); }
                CurrentNode.Context.ManualLevel = ManualLevel;
                bool isExit = await CurrentNode.GoAsync();
                if (isExit)
                { NodeExitChanged?.Invoke(this, CurrentNode.Name); }
                if (CurrentNode.Context.IsPaused)
                {
                    while (EventConsumer.Reader.Count > 0)
                    { midEventList.Enqueue(await EventConsumer.Reader.ReadAsync()); }
                    pausing = false;
                }
            }
            catch (OperationCanceledException) { isCancel = true; }
            catch (Exception ex) when (ex.InnerException is OperationCanceledException) { isCancel = true; }
            catch (Exception ex)
            {
                NodeExceptionEvent?.Invoke(ex);
                throw new FSMException($"流程{CurrentNode.Name}运行异常");
            }
            finally
            {
                CurrentNode.RaiseInterrupt -= CurrentNode_RaiseInterrupt;
                CurrentNode.RaisePause -= CurrentNode_RaisePause;
            }

            return isCancel;
        }

        public event Action<Exception> NodeExceptionEvent;

        private async Task ConsumerTask()
        {
            long threadId = Thread.CurrentThread.ManagedThreadId;
            Thread.CurrentThread.Name = $"State Machine Task({threadId})";
            using ((this as IObservable<StateTrackInfo>).Subscribe(observer))
            {
                eventAggregator.Subscribe(this);
                try
                {
                    bool isCancel = false;

                    //这里是第一个启动节点
                    //Track Start Enter
                    TrackState(new StateTrackInfo()
                    {
                        IsEnter = true,
                        TrackType = TrackType.Start,

                        PrevStateName = "",
                        CurrentNode = Start,
                        StateName = Start.Name,

                        FSMEvent = default!,
                        EventName = "",

                        ThreadId = threadId,
                    });
                    isCancel = await RunCurrentNodeAsync(true);

                    //Track Start Exit
                    TrackState(new StateTrackInfo()
                    {
                        IsEnter = false,
                        TrackType = isCancel ? TrackType.Cancel : TrackType.Normal,

                        PrevStateName = "",
                        CurrentNode = Start,
                        StateName = Start.Name,

                        FSMEvent = default!,
                        EventName = "",

                        ThreadId = threadId,
                    });
                    while (await EventConsumer.Reader.WaitToReadAsync())
                    {
                        while (EventConsumer.Reader.TryRead(out FSMEvent? @event))
                        {
                            if (@event.EventID == ContinueEvent.EventID)
                            {
                                //这里是暂停之后继续的分支
                                //Track Continue Enter
                                TrackState(new StateTrackInfo()
                                {
                                    IsEnter = true,
                                    TrackType = TrackType.Continue,

                                    PrevStateName = CurrentNode.Name,
                                    CurrentNode = CurrentNode,
                                    StateName = CurrentNode.Name,

                                    EventName = ContinueEvent.EventName,
                                    FSMEvent = ContinueEvent,

                                    ThreadId = threadId,
                                });

                                isCancel = await RunCurrentNodeAsync(false);

                                //Track Continue Exit
                                TrackState(new StateTrackInfo()
                                {
                                    IsEnter = false,
                                    TrackType = isCancel ? TrackType.Cancel : TrackType.Normal,

                                    PrevStateName = "",
                                    CurrentNode = CurrentNode,
                                    StateName = CurrentNode.Name,

                                    FSMEvent = default!,
                                    EventName = "",

                                    ThreadId = threadId,
                                });
                            }
                            else if (CurrentNode.HasTransition(@event))
                            {
                                //这里是正常执行节点的分支
                                CurrentNode.Context.TriggerEvent = @event;
                                SolverContext.LastNodeName = CurrentNode.Name;
                                var nextNode = CurrentNode.TargetState(@event);
                                nextNode.Context = CurrentNode.Context;

                                //Track Enter
                                TrackState(new StateTrackInfo()
                                {
                                    IsEnter = true,
                                    TrackType = TrackType.Normal,

                                    PrevStateName = CurrentNode.Name,
                                    StateName = nextNode.Name,
                                    CurrentNode = nextNode,

                                    FSMEvent = @event,
                                    EventName = @event.EventName,

                                    ThreadId = threadId,
                                });

                                CurrentNode = nextNode;

                                isCancel = await RunCurrentNodeAsync(true);

                                //Track Exit
                                TrackState(new StateTrackInfo()
                                {
                                    IsEnter = false,
                                    TrackType = isCancel ? TrackType.Cancel : TrackType.Normal,

                                    PrevStateName = CurrentNode.Name,
                                    StateName = CurrentNode.Name,
                                    CurrentNode = CurrentNode,

                                    FSMEvent = default!,
                                    EventName = "",

                                    ThreadId = threadId,
                                });
                            }
                            else
                            {
                                //无用的Event
                                TrackState(new StateTrackInfo()
                                {
                                    TrackType = TrackType.DiscardEvent,

                                    PrevStateName = "",
                                    CurrentNode = CurrentNode,
                                    StateName = CurrentNode.Name,

                                    FSMEvent = @event,
                                    EventName = @event.EventName,

                                    ThreadId = threadId,
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Track Exit
                    TrackState(new StateTrackInfo()
                    {
                        IsEnter = false,
                        TrackType = TrackType.StateError,

                        PrevStateName = CurrentNode.Name,
                        StateName = CurrentNode.Name,
                        CurrentNode = CurrentNode,

                        FSMEvent = default!,
                        EventName = "",

                        ThreadId = threadId,
                    });

                    throw new FSMException($"Consumer Task Exit with error: {ex.Message}");
                }
                finally
                { eventAggregator.Unsubscribe(this); }
            }
        }

        private void CurrentNode_RaiseInterrupt()
        {
            eventAggregator.Publish(InteruptEvent);
        }

        private void CurrentNode_RaisePause()
        {
            eventAggregator.Publish(PauseEvent);
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
            yield return Start;
            foreach (var t in Traversal(Start, new ConcurrentBag<IFSMNode>()))
                yield return t;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Stop()
        {
            TrackCallname();
            State = FSMNodeState.Stopping;
            while (EventConsumer.Reader.Count > 0)
            { EventConsumer.Reader.TryRead(out _); }
            Exception e = default!;
            Task.Run(async () =>
            {
                if (CurrentNode != null && !CurrentNode.Context.IsPaused)
                {
                    CurrentNode.Context.Pause();
                    try
                    {
                        await CurrentNode.WaitCurrentTask;
                    }
                    catch (Exception ex)
                    {
                        e = ex;
                    }
                }
                while (EventConsumer.Reader.Count > 0)
                { EventConsumer.Reader.TryRead(out _); }
                EventConsumer.Writer.TryComplete();
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
                State = FSMNodeState.Stoped;
            });
            return;
        }

        public async Task<bool> StopAsync()
        {
            TrackCallname();
            State = FSMNodeState.Stopping;
            while (EventConsumer.Reader.Count > 0)
            { EventConsumer.Reader.TryRead(out _); }
            Exception e = default!;
            if (CurrentNode != null && !CurrentNode.Context.IsPaused)
            {
                CurrentNode.Context.Pause();
                try
                {
                    await CurrentNode.WaitCurrentTask;
                }
                catch (Exception ex)
                {
                    e = ex;
                }
            }
            while (EventConsumer.Reader.Count > 0)
            { EventConsumer.Reader.TryRead(out _); }
            EventConsumer.Writer.TryComplete();
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
            State = FSMNodeState.Stoped;
            return true;
        }

        private void InitNodes()
        {
            Start.InitBeforeStart();
            foreach (var child in Start)
            {
                child.InitBeforeStart();
            }
        }

        public async Task<bool> RestartAsync()
        {
            TrackCallname();
            if (!await StopAsync())
            { return false; }
            if (EventConsumer != null && !EventConsumer.Reader.Completion.IsCompleted)
            { return false; }

            CurrentNode = Start;

            pausing = false;
            if (Start.Context == null)
            { Start.Context = new FSMNodeContext(); }
            while (midEventList.TryDequeue(out _)) { }
            EventConsumer = Channel.CreateUnbounded<FSMEvent>();

            InitNodes();
            ExecutorTask = Task.Run(ConsumerTask);

            return true;
        }

        public async Task<bool> RestartAsync(FSMNodeContext context)
        {
            if (!await StopAsync())
            { return false; }
            Start.Context = context;
            return await RestartAsync();
        }

        public async Task<bool> RestartAsync(IFSMNode node)
        {
            TrackCallname();
            if (!await StopAsync())
            { return false; }
            if (EventConsumer != null && !EventConsumer.Reader.Completion.IsCompleted)
            { return false; }

            CurrentNode = node ?? throw new FSMException("开始节点不能为空！");

            pausing = false;
            if (node.Context == null)
            { node.Context = new FSMNodeContext(); }
            while (midEventList.TryDequeue(out _)) { }
            EventConsumer = Channel.CreateUnbounded<FSMEvent>();

            InitNodes();
            ExecutorTask = Task.Run(ConsumerTask);

            return true;
        }

        public async Task<bool> RestartAsync(IFSMNode node, FSMNodeContext context)
        {
            if (!await StopAsync())
            { return false; }
            node.Context = context;
            return await RestartAsync(node);
        }

        private ConcurrentQueue<FSMEvent> midEventList = new ConcurrentQueue<FSMEvent>();
        private bool pausing = false;

        //暂停
        public void Pause()
        {
            TrackCallname();
            if (CurrentNode.Context.IsPaused || pausing)
            { return; }

            State = FSMNodeState.Pausing;
            pausing = true;
            while (EventConsumer.Reader.Count > 0)
            {
                if (EventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                {
                    midEventList.Enqueue(fSMEvent);
                }
            }
            CurrentNode.Context.Pause();
            Task.Run(async () =>
            {
                try
                {
                    await CurrentNode.WaitCurrentTask;
                }
                finally
                {
                    while (EventConsumer.Reader.Count > 0)
                    {
                        if (EventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                        {
                            midEventList.Enqueue(fSMEvent);
                        }
                    }
                    pausing = false;
                    State = FSMNodeState.Paused;
                }
            });
            return;
        }

        public async Task<bool> PauseAsync()
        {
            TrackCallname();
            if (CurrentNode.Context.IsPaused || pausing)
            { return false; }

            State = FSMNodeState.Pausing;
            pausing = true;
            while (EventConsumer.Reader.Count > 0)
            {
                if (EventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                {
                    midEventList.Enqueue(fSMEvent);
                }
            }
            CurrentNode.Context.Pause();
            try
            {
                await CurrentNode.WaitCurrentTask;
            }
            finally
            {
                while (EventConsumer.Reader.Count > 0)
                {
                    if (EventConsumer.Reader.TryRead(out FSMEvent? fSMEvent))
                    {
                        midEventList.Enqueue(fSMEvent);
                    }
                }
                pausing = false;
                State = FSMNodeState.Paused;
            }
            return true;
        }

        //继续
        public bool Continue()
        {
            TrackCallname();
            if (CurrentNode.Context.IsPaused || pausing)
            {
                State = FSMNodeState.Proceeding;
                EventConsumer.Writer.TryWrite(ContinueEvent);
                while (midEventList.TryDequeue(out FSMEvent? e))
                { EventConsumer.Writer.TryWrite(e); }
                State = FSMNodeState.Running;
                return true;
            }

            return false;
        }

        //打断
        public bool Interupt()
        {
            TrackCallname();
            if (CurrentNode.Context.IsPaused || pausing)
            { return true; }

            CurrentNode.Context.Pause();
            State = FSMNodeState.Interrupted;
            return true;
        }

        public void Handle(FSMEvent @event)
        {
            if (@event.EventID == EndEvent.EventID)
            {
                EventConsumer.Writer.TryComplete();
                State = FSMNodeState.Finished;
            }
            else if (@event.EventID == PauseEvent.EventID)
            {
                Pause();
            }
            else if (@event.EventID == InteruptEvent.EventID)
            {
                Interupt();
            }
            else if (CurrentNode.Context.IsPaused)
            {
                midEventList.Enqueue(@event);
            }
            else if (!EventConsumer.Reader.Completion.IsCompleted)
            {
                EventConsumer.Writer.TryWrite(@event);
            }
        }
    }



    public partial class FSMSingleThreadExecutor : IObservable<StateTrackInfo>, IObservable<string>
    {
        //流程节点进出事件
        public event Action<StateTrackInfo> TrackStateEvent;

        //FSMNode类的方法调用事件
        public event Action<string> TrackCallEvent;

        private FSMNodeObserver observer;
        private class FSMNodeObserver : IObserver<StateTrackInfo>, IObserver<string>
        {
            private FSMSingleThreadExecutor solver;
            public FSMNodeObserver(FSMSingleThreadExecutor solver_)
            {
                solver = solver_;
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(StateTrackInfo value)
            {
                solver.TrackStateEvent?.Invoke(value);
            }

            public void OnNext(string value)
            {
                solver.TrackCallEvent?.Invoke(value);
            }
        }

        private List<IObserver<StateTrackInfo>> observers = new List<IObserver<StateTrackInfo>>();
        IDisposable IObservable<StateTrackInfo>.Subscribe(IObserver<StateTrackInfo> observer)
        {
            observers.Add(observer);
            return new Unsubscriber<StateTrackInfo>(observers, observer);
        }

        private void TrackState(StateTrackInfo info)
        {
            foreach (var observer in observers)
                observer.OnNext(info);
        }

        public void StopTrack()
        {
            foreach (var observer in observers)
                observer.OnCompleted();
            foreach (var observer in stringObservers)
                observer.OnCompleted();

            observers.Clear();
            stringObservers.Clear();
        }

        private void TrackCallname([CallerMemberName] string info = default!)
        {
            foreach (var observer in stringObservers)
                observer.OnNext(info);
        }

        private List<IObserver<string>> stringObservers = new List<IObserver<string>>();
        IDisposable IObservable<string>.Subscribe(IObserver<string> observer)
        {
            stringObservers.Add(observer);
            return new Unsubscriber<string>(stringObservers, observer);
        }

        private class Unsubscriber<T> : IDisposable
        {
            private List<IObserver<T>> _observers;
            private IObserver<T> _observer;

            public Unsubscriber(List<IObserver<T>> observers, IObserver<T> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (_observer != null && _observers.Contains(_observer))
                    _observers.Remove(_observer);
            }
        }
    }

    public partial class FSMSingleThreadExecutor : IDisposable
    {
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                    observerDisposeObj?.Dispose();
                    if (EventConsumer != null)
                    {
                        eventAggregator.Unsubscribe(this);
                        EventConsumer.Writer.TryComplete();
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
                    }
                }
                // TODO: 释放未托管的资源(未托管的对象)并替代终结器


                // TODO: 将大型字段设置为 null
                EventConsumer = default!;
                ExecutorTask = default!;

                disposedValue = true;
            }
        }

        // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        ~FSMSingleThreadExecutor()
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
