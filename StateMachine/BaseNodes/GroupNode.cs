using StateMachine.Interface;
using System;
using System.Reactive.Linq;

namespace StateMachine
{
    internal interface IGroupNode
    {
        public string StartNode { get; set; }

        public string EndEvent { get; set; }

    }

    [FSMNode("Group", "流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 3)]
    public class GroupNode : BaseGroupNode, IGroupNode
    {

        private FSMExecutor? executor;

        [Obsolete("use StartNode instead of")]
        public string StartName { get => StartNode; set { StartNode = value; } }

        [FSMProperty("Start node's name", true, 3)]
        public string StartNode { get; set; } = default!;

        [FSMProperty("End event's name", true, 4)]
        public string EndEvent { get; set; } = default!;

        public object ContextData { get; set; } = default!;

        public override void InitBeforeStart()
        {
            executor?.Dispose();
            if (Engine is not null)
            {
                try
                {
                    executor = new FSMExecutor(Engine[StartNode], Engine.GetEvent(EndEvent));
                }
                catch (Exception)
                {
                }
            }
        }

        public GroupNode()
        {
        }

        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            if (executor is null)
            {
                PublishEvent(FSMEnum.Error);
                yield break;
            }
            if (executor.State == FSMState.Paused)
            {
                executor.Continue();
            }
            else
            {
                bool isLongRunning = false;
                if (SynchronizationContext.Current is FSMSyncContext)
                {
                    isLongRunning = true;
                }
                executor.SolverContext = this.ExecuterContext;
                if (ContextData is not null)
                {
                    await executor.RestartAsync(ContextData, isLongRunning);
                }
                else
                {
                    await executor.RestartAsync(Context.Data, isLongRunning);
                }
            }
            yield return Yield.None;
            executor.FSMStateChanged += Executor_FSMStateChanged;
            using (Context.Token.Register(executor.Pause))
            {
                try
                {
                    await Task.WhenAny(executor.ExecutorTask, Task.Delay(-1, Context.Token));
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    executor.FSMStateChanged -= Executor_FSMStateChanged;
                }
            }
            if (Context.IsPaused)
            {
                try
                {
                    await executor.CurrentNodeTask;
                }
                catch (Exception)
                {
                }
                yield return Yield.ToNodeStart;
            }
            yield return Yield.None;
            if (executor.State == FSMState.Finished)
            {
                PublishEvent(FSMEnum.Next);
            }
            else if (executor.State == FSMState.Stoped)
            {
                PublishEvent(FSMEnum.Cancel);
            }
        }

        private void Executor_FSMStateChanged(FSMExecutor arg1, FSMState newState, FSMState oldState)
        {
            if (newState == FSMState.Paused && oldState != FSMState.Paused)
            {
                this.Pause();
            }
        }

        protected override void Dispose(bool disposing)
        {
            executor?.Dispose();
            base.Dispose(disposing);
        }

        public override IDisposable Subscribe(IObserver<StateTrackInfo> observer)
        {
            return ((IObservable<StateTrackInfo>)executor).Subscribe(observer);
        }

        public override IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer)
        {
            return ((IObservable<ExecuteTrackInfo>)executor).Subscribe(observer);
        }
    }


    [FSMNode("GroupT", "流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 3)]
    public class GroupNode<T> : BaseGroupNode<T>, IGroupNode where T : class
    {

        private FSMExecutor? executor;

        [Obsolete("use StartNode instead of")]
        public string StartName { get => StartNode; set { StartNode = value; } }

        [FSMProperty("Start node's name", true, 3)]
        public string StartNode { get; set; } = default!;

        [FSMProperty("End event's name", true, 4)]
        public string EndEvent { get; set; } = default!;

        public T ContextData { get; set; } = default!;

        public override void InitBeforeStart()
        {
            executor?.Dispose();
            if (Engine is not null)
            {
                try
                {
                    executor = new FSMExecutor(Engine[StartNode], Engine.GetEvent(EndEvent));
                }
                catch (Exception)
                {
                }
            }
        }

        public GroupNode()
        {
        }

        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            if (executor is null)
            {
                PublishEvent(FSMEnum.Error);
                yield break;
            }
            if (executor.State == FSMState.Paused)
            {
                executor.Continue();
            }
            else
            {
                bool isLongRunning = false;
                if(SynchronizationContext.Current is FSMSyncContext)
                {
                    isLongRunning = true;
                }
                executor.SolverContext = this.ExecuterContext;
                if (ContextData is not null)
                {
                    await executor.RestartAsync(ContextData, isLongRunning);
                }
                else
                {
                    await executor.RestartAsync(Context.Data, isLongRunning);
                }
            }
            yield return Yield.None;
            executor.FSMStateChanged += Executor_FSMStateChanged;
            using (Context.Token.Register(executor.Pause))
            {
                try
                {
                    await Task.WhenAny(executor.ExecutorTask, Task.Delay(-1, Context.Token));
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    executor.FSMStateChanged -= Executor_FSMStateChanged;
                }
            }
            if (Context.IsPaused)
            {
                try
                {
                    await executor.CurrentNodeTask;
                }
                catch (Exception)
                {
                }
                yield return Yield.ToNodeStart;
            }
            yield return Yield.None;
            if (executor.State == FSMState.Finished)
            {
                PublishEvent(FSMEnum.Next);
            }
            else if (executor.State == FSMState.Stoped)
            {
                PublishEvent(FSMEnum.Cancel);
            }
        }

        private void Executor_FSMStateChanged(FSMExecutor arg1, FSMState newState, FSMState oldState)
        {
            if (newState == FSMState.Paused && oldState != FSMState.Paused)
            {
                this.Pause();
            }
        }

        protected override void Dispose(bool disposing)
        {
            executor?.Dispose();
            base.Dispose(disposing);
        }

        public override IDisposable Subscribe(IObserver<StateTrackInfo> observer)
        {
            return ((IObservable<StateTrackInfo>)executor).Subscribe(observer);
        }

        public override IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer)
        {
            return ((IObservable<ExecuteTrackInfo>)executor).Subscribe(observer);
        }
    }


    [FSMNode("GroupTU", "流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 3)]
    public class GroupNode<T, U> : BaseGroupNode<T>, IGroupNode where T : class where U : class
    {

        private FSMExecutor? executor;

        [Obsolete("use StartNode instead of")]
        public string StartName { get => StartNode; set { StartNode = value; } }

        [FSMProperty("Start node's name", true, 3)]
        public string StartNode { get; set; } = default!;

        [FSMProperty("End event's name", true, 4)]
        public string EndEvent { get; set; } = default!;

        public U ContextData { get; set; } = default!;

        public override void InitBeforeStart()
        {
            executor?.Dispose();
            if (Engine is not null)
            {
                try
                {
                    executor = new FSMExecutor(Engine[StartNode], Engine.GetEvent(EndEvent));
                }
                catch (Exception)
                {
                }
            }
        }

        public GroupNode()
        {
        }

        protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable()
        {
            yield return Yield.None;
            if (executor is null)
            {
                PublishEvent(FSMEnum.Error);
                yield break;
            }
            if (executor.State == FSMState.Paused)
            {
                executor.Continue();
            }
            else
            {
                bool isLongRunning = false;
                if (SynchronizationContext.Current is FSMSyncContext)
                {
                    isLongRunning = true;
                }
                executor.SolverContext = this.ExecuterContext;
                if (ContextData is not null)
                {
                    await executor.RestartAsync(ContextData, isLongRunning);
                }
                else
                {
                    await executor.RestartAsync(Context.Data, isLongRunning);
                }
            }
            yield return Yield.None;
            executor.FSMStateChanged += Executor_FSMStateChanged;
            using (Context.Token.Register(executor.Pause))
            {
                try
                {
                    await Task.WhenAny(executor.ExecutorTask, Task.Delay(-1, Context.Token));
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    executor.FSMStateChanged -= Executor_FSMStateChanged;
                }
            }
            if (Context.IsPaused)
            {
                try
                {
                    await executor.CurrentNodeTask;
                }
                catch (Exception)
                {
                }
                yield return Yield.ToNodeStart;
            }
            yield return Yield.None;
            if (executor.State == FSMState.Finished)
            {
                PublishEvent(FSMEnum.Next);
            }
            else if (executor.State == FSMState.Stoped)
            {
                PublishEvent(FSMEnum.Cancel);
            }
        }

        private void Executor_FSMStateChanged(FSMExecutor arg1, FSMState newState, FSMState oldState)
        {
            if (newState == FSMState.Paused && oldState != FSMState.Paused)
            {
                this.Pause();
            }
        }

        protected override void Dispose(bool disposing)
        {
            executor?.Dispose();
            base.Dispose(disposing);
        }

        public override IDisposable Subscribe(IObserver<StateTrackInfo> observer)
        {
            return ((IObservable<StateTrackInfo>)executor).Subscribe(observer);
        }

        public override IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer)
        {
            return ((IObservable<ExecuteTrackInfo>)executor).Subscribe(observer);
        }
    }
}

