using StateMachine.Interface;
using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace StateMachine
{
    internal interface IParallelNode
    {
        public List<FSMDescribe> FSMs { get; set; }
    }

    public class FSMDescribe
    {
        public string StartNode { get; set; } = "";

        public string EndEvent { get; set; } = "";
    }

    [FSMNode("Parallel", "并行流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 4)]
    public class ParallelNode : BaseGroupNode, IParallelNode
    {

        private List<FSMExecutor> executors = [];

        [FSMProperty("Parrllel FSM", true, 3)]
        public List<FSMDescribe> FSMs { get; set; } = [];

        public List<FSMNodeContext> ContextDatas { get; set; } = [];

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


        protected virtual async IAsyncEnumerable<IYieldAction> BeforeExecuteEnumerable()
        {
            yield break;
        }

        protected override sealed async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            await foreach (var item in BeforeExecuteEnumerable())
            {
                yield return item;
            }
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
                    if (ContextDatas.Count > i && ContextDatas[i] is not null)
                    {
                        await executor.RestartAsync(ContextDatas[i], isLongRunning);
                    }
                    else
                    {
                        await executor.RestartAsync(Context.Data, isLongRunning);
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

        private protected override string GroupScript()
        {
            string r = "";
            foreach(var fsm in FSMs)
            {
                r += $"\r\n\t[{fsm.StartNode}->{fsm.EndEvent}];";
            }
            return r;
        }
    }

    [FSMNode("ParallelT", "并行流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 4)]
    public class ParallelNode<T> : BaseGroupNode<T>, IParallelNode where T : class
    {

        private List<FSMExecutor> executors = [];

        [FSMProperty("Parrllel FSM", true, 3)]
        public List<FSMDescribe> FSMs { get; set; } = [];

        public List<FSMNodeContext<T>> ContextDatas { get; set; } = [];

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


        protected virtual async IAsyncEnumerable<IYieldAction> BeforeExecuteEnumerable()
        {
            yield break;
        }

        protected override sealed async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            await foreach (var item in BeforeExecuteEnumerable())
            {
                yield return item;
            }
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
                    if (ContextDatas.Count > i && ContextDatas[i] is not null)
                    {
                        await executor.RestartAsync(ContextDatas[i], isLongRunning);
                    }
                    else
                    {
                        await executor.RestartAsync(Context.Data, isLongRunning);
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

        private protected override string GroupScript()
        {
            string r = "";
            foreach (var fsm in FSMs)
            {
                r += $"\r\n\t[{fsm.StartNode}->{fsm.EndEvent}];";
            }
            return r;
        }
    }

    [FSMNode("ParallelTU", "并行流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 4)]
    public class ParallelNode<T, U> : BaseGroupNode<T>, IParallelNode where T : class where U : class
    {

        private List<FSMExecutor> executors = [];

        [FSMProperty("Parrllel FSM", true, 3)]
        public List<FSMDescribe> FSMs { get; set; } = [];

        public List<FSMNodeContext<U>> ContextDatas { get; set; } = [];

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


        protected virtual async IAsyncEnumerable<IYieldAction> BeforeExecuteEnumerable()
        {
            yield break;
        }

        protected override sealed async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            await foreach (var item in BeforeExecuteEnumerable())
            {
                yield return item;
            }
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
                    if (ContextDatas.Count > i && ContextDatas[i] is not null)
                    {
                        await executor.RestartAsync(ContextDatas[i], isLongRunning);
                    }
                    else
                    {
                        await executor.RestartAsync(Context.Data, isLongRunning);
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

        private protected override string GroupScript()
        {
            string r = "";
            foreach (var fsm in FSMs)
            {
                r += $"\r\n\t[{fsm.StartNode}->{fsm.EndEvent}];";
            }
            return r;
        }
    }
}
