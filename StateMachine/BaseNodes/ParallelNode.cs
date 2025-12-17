using StateMachine.Interface;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace StateMachine
{
    public class FSMDescribe
    {
        public string StartNode { get; set; } = "";

        public string EndEvent { get; set; } = "";

        public object ContextData { get; set; }
    }

    public class FSMDescribe<T>
    {
        public string StartNode { get; set; } = "";

        public string EndEvent { get; set; } = "";

        public T ContextData { get; set; }
    }

    [FSMNode("Parallel", "并行流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 4)]
    public class ParallelNode : BaseGroupNode
    {

        private List<FSMExecutor> executors = [];

        [FSMProperty("Parrllel FSM", true, 3)]
        public List<FSMDescribe> FSMs { get; set; } = [];

        public override void InitBeforeStart()
        {
            executors.ForEach(p => p.Dispose());
            executors.Clear();
            foreach (var fsm in FSMs)
            {
                var executor = new FSMExecutor(Engine[fsm.StartNode], Engine.GetEvent(fsm.EndEvent));
                executors.Add(executor);
            }
        }

        public ParallelNode()
        {
        }

        public ParallelNode(List<FSMDescribe> fsms)
        {
            this.FSMs = fsms;
        }

        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            if (executors.Any(p => p.State == FSMState.Paused))
            {
                foreach (var executor in executors)
                {
                    if (!executor.ExecutorTask.IsCompleted)
                        executor.Continue();
                }
            }
            else
            {
                bool isLongRunning = false;
                if (SynchronizationContext.Current is FSMSyncContext)
                {
                    isLongRunning = true;
                }
                foreach (var (executor, i) in executors.Select((p, i)=>(p, i)))
                {
                    executor.SolverContext = this.ExecuterContext;
                    if (FSMs[i].ContextData is not null)
                    {
                        await executor.RestartAsync(FSMs[i].ContextData, isLongRunning);
                    }
                    else
                    {
                        await executor.RestartAsync(Context, isLongRunning);
                    }
                }
            }
            yield return Yield.None;
            using (Context.Token
                    .Register(() =>
                    {
                        foreach (var executor in executors)
                        {
                            if (!executor.ExecutorTask.IsCompleted)
                                executor.Pause();
                        }
                    })
                    )
            {
                try
                {
                    executors.ForEach(p => p.FSMStateChanged += Executor_FSMStateChanged);
                    await Task.WhenAny(Task.WhenAll(executors.Select(p => p.ExecutorTask)), Task.Delay(-1, Context.Token));
                }
                catch (OperationCanceledException)
                { }
                finally
                {
                    executors.ForEach(p => p.FSMStateChanged -= Executor_FSMStateChanged);
                }
            }
            if (Context.IsPaused)
            {
                try
                {
                    await Task.WhenAll(executors.Select(p => p.CurrentNodeTask));
                }
                catch (Exception)
                {
                }
                yield return Yield.ToNodeStart;
            }
            yield return Yield.None;
            if (executors.Any(p => p.State == FSMState.Stoped))
            {
                PublishEvent(FSMEnum.Cancel);
            }
            else
            {
                PublishEvent(FSMEnum.Next);
            }
        }

        private void Executor_FSMStateChanged(FSMExecutor executor, FSMState state1, FSMState state2)
        {
            if (state1 == FSMState.Paused)
            {
                this.Pause();
            }
        }

        protected override void Dispose(bool disposing)
        {
            executors.ForEach(p => p.Dispose());
            base.Dispose(disposing);
        }

        public override IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer)
        {
            CompositeDisposable disposables = new CompositeDisposable();
            foreach(var executor in executors)
            {
                disposables.Add(((IObservable<ExecuteTrackInfo>)executor).Subscribe(observer));
            }
            return disposables;
        }

        public override IDisposable Subscribe(IObserver<StateTrackInfo> observer)
        {
            CompositeDisposable disposables = new CompositeDisposable();
            foreach (var executor in executors)
            {
                disposables.Add(((IObservable<StateTrackInfo>)executor).Subscribe(observer));
            }
            return disposables;
        }

    }

    [FSMNode("ParallelT", "并行流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 4)]
    public class ParallelNode<T> : BaseGroupNode<T> where T : class
    {

        private List<FSMExecutor> executors = [];

        [FSMProperty("Parrllel FSM", true, 3)]
        public List<FSMDescribe<T>> FSMs { get; set; } = [];

        public override void InitBeforeStart()
        {
            executors.ForEach(p => p.Dispose());
            executors.Clear();
            foreach (var fsm in FSMs)
            {
                var executor = new FSMExecutor(Engine[fsm.StartNode], Engine.GetEvent(fsm.EndEvent));
                executors.Add(executor);
            }
        }

        public ParallelNode()
        {
        }

        public ParallelNode(List<FSMDescribe<T>> fsms)
        {
            this.FSMs = fsms;
        }

        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            if (executors.Any(p => p.State == FSMState.Paused))
            {
                foreach (var executor in executors)
                {
                    if (!executor.ExecutorTask.IsCompleted)
                        executor.Continue();
                }
            }
            else
            {
                bool isLongRunning = false;
                if (SynchronizationContext.Current is FSMSyncContext)
                {
                    isLongRunning = true;
                }
                foreach (var (executor, i) in executors.Select((p, i) => (p, i)))
                {
                    executor.SolverContext = this.ExecuterContext;
                    if (FSMs[i].ContextData is not null)
                    {
                        await executor.RestartAsync(FSMs[i].ContextData, isLongRunning);
                    }
                    else
                    {
                        await executor.RestartAsync(Context, isLongRunning);
                    }
                }
            }
            yield return Yield.None;
            using (Context.Token
                    .Register(() =>
                    {
                        foreach (var executor in executors)
                        {
                            if (!executor.ExecutorTask.IsCompleted)
                                executor.Pause();
                        }
                    })
                    )
            {
                try
                {
                    executors.ForEach(p => p.FSMStateChanged += Executor_FSMStateChanged);
                    await Task.WhenAny(Task.WhenAll(executors.Select(p => p.ExecutorTask)), Task.Delay(-1, Context.Token));
                }
                catch (OperationCanceledException)
                { }
                finally
                {
                    executors.ForEach(p => p.FSMStateChanged -= Executor_FSMStateChanged);
                }
            }
            if (Context.IsPaused)
            {
                try
                {
                    await Task.WhenAll(executors.Select(p => p.CurrentNodeTask));
                }
                catch (Exception)
                {
                }
                yield return Yield.ToNodeStart;
            }
            yield return Yield.None;
            if (executors.Any(p => p.State == FSMState.Stoped))
            {
                PublishEvent(FSMEnum.Cancel);
            }
            else
            {
                PublishEvent(FSMEnum.Next);
            }
        }

        private void Executor_FSMStateChanged(FSMExecutor executor, FSMState state1, FSMState state2)
        {
            if (state1 == FSMState.Paused)
            {
                this.Pause();
            }
        }

        protected override void Dispose(bool disposing)
        {
            executors.ForEach(p => p.Dispose());
            base.Dispose(disposing);
        }

        public override IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer)
        {
            CompositeDisposable disposables = new CompositeDisposable();
            foreach (var executor in executors)
            {
                disposables.Add(((IObservable<ExecuteTrackInfo>)executor).Subscribe(observer));
            }
            return disposables;
        }

        public override IDisposable Subscribe(IObserver<StateTrackInfo> observer)
        {
            CompositeDisposable disposables = new CompositeDisposable();
            foreach (var executor in executors)
            {
                disposables.Add(((IObservable<StateTrackInfo>)executor).Subscribe(observer));
            }
            return disposables;
        }
    }

    [FSMNode("ParallelTU", "并行流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 4)]
    public class ParallelNode<T, U> : BaseGroupNode<T> where T : class where U : class
    {

        private List<FSMExecutor> executors = [];

        [FSMProperty("Parrllel FSM", true, 3)]
        public List<FSMDescribe<U>> FSMs { get; set; } = [];

        public override void InitBeforeStart()
        {
            executors.ForEach(p => p.Dispose());
            executors.Clear();
            foreach (var fsm in FSMs)
            {
                var executor = new FSMExecutor(Engine[fsm.StartNode], Engine.GetEvent(fsm.EndEvent));
                executors.Add(executor);
            }
        }

        public ParallelNode()
        {
        }

        public ParallelNode(List<FSMDescribe<U>> fsms)
        {
            this.FSMs = fsms;
        }

        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            if (executors.Any(p => p.State == FSMState.Paused))
            {
                foreach (var executor in executors)
                {
                    if (!executor.ExecutorTask.IsCompleted)
                        executor.Continue();
                }
            }
            else
            {
                bool isLongRunning = false;
                if (SynchronizationContext.Current is FSMSyncContext)
                {
                    isLongRunning = true;
                }
                foreach (var (executor, i) in executors.Select((p, i) => (p, i)))
                {
                    executor.SolverContext = this.ExecuterContext;
                    if (FSMs[i].ContextData is not null)
                    {
                        await executor.RestartAsync(FSMs[i].ContextData, isLongRunning);
                    }
                    else
                    {
                        await executor.RestartAsync(Context, isLongRunning);
                    }
                }
            }
            yield return Yield.None;
            using (Context.Token
                    .Register(() =>
                    {
                        foreach (var executor in executors)
                        {
                            if (!executor.ExecutorTask.IsCompleted)
                                executor.Pause();
                        }
                    })
                    )
            {
                try
                {
                    executors.ForEach(p => p.FSMStateChanged += Executor_FSMStateChanged);
                    await Task.WhenAny(Task.WhenAll(executors.Select(p => p.ExecutorTask)), Task.Delay(-1, Context.Token));
                }
                catch (OperationCanceledException)
                { }
                finally
                {
                    executors.ForEach(p => p.FSMStateChanged -= Executor_FSMStateChanged);
                }
            }
            if (Context.IsPaused)
            {
                try
                {
                    await Task.WhenAll(executors.Select(p => p.CurrentNodeTask));
                }
                catch (Exception)
                {
                }
                yield return Yield.ToNodeStart;
            }
            yield return Yield.None;
            if (executors.Any(p => p.State == FSMState.Stoped))
            {
                PublishEvent(FSMEnum.Cancel);
            }
            else
            {
                PublishEvent(FSMEnum.Next);
            }
        }

        private void Executor_FSMStateChanged(FSMExecutor executor, FSMState state1, FSMState state2)
        {
            if (state1 == FSMState.Paused)
            {
                this.Pause();
            }
        }

        protected override void Dispose(bool disposing)
        {
            executors.ForEach(p => p.Dispose());
            base.Dispose(disposing);
        }

        public override IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer)
        {
            CompositeDisposable disposables = new CompositeDisposable();
            foreach (var executor in executors)
            {
                disposables.Add(((IObservable<ExecuteTrackInfo>)executor).Subscribe(observer));
            }
            return disposables;
        }

        public override IDisposable Subscribe(IObserver<StateTrackInfo> observer)
        {
            CompositeDisposable disposables = new CompositeDisposable();
            foreach (var executor in executors)
            {
                disposables.Add(((IObservable<StateTrackInfo>)executor).Subscribe(observer));
            }
            return disposables;
        }
    }
}
