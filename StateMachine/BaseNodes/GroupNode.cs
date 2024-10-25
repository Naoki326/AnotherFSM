using StateMachine.Interface;

namespace StateMachine
{
    [FSMNode("Group", "流程包装节点", [1, 3, 5], ["NextEvent", "ErrorEvent", "CancelEvent"], Id = 3)]
    public class GroupNode : BaseGroupNode
    {

        private FSMExecutor? executor;

        [FSMProperty("Start node's name", true, 3)]
        public string StartName { get; set; } = default!;

        [FSMProperty("End event's name", true, 4)]
        public string EndEvent { get; set; } = default!;

        public override void InitBeforeStart()
        {
            executor?.Dispose();
            if (Engine is not null)
            {
                try
                {
                    executor = new FSMExecutor(Engine[StartName], Engine.GetEvent(EndEvent));
                    executor.NodeStateChanged += OnNodeStateChanged;
                    executor.NodeExitChanged += OnNodeExitChanged;
                }
                catch (Exception)
                {
                }
            }
        }

        public GroupNode()
        {
        }

        public GroupNode(string startName, string endEvent)
        {
            this.StartName = startName;
            this.EndEvent = endEvent;
        }

        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
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
                await executor.RestartAsync();
            }
            yield return Yield.None;
            executor.FSMStateChanged += Executor_FSMStateChanged;
            using (Context.TokenSource.Token.Register(executor.Pause))
            {
                try
                {
                    await Task.WhenAny(executor.ExecutorTask, Task.Delay(-1, Context.Token));
                    if (Context.IsPaused)
                    {
                        await Task.WhenAny(executor.CurrentNodeTask, Task.Delay(-1, Context.Token));
                    }
                }
                catch (OperationCanceledException ex)
                {
                }
                finally
                {
                    executor.FSMStateChanged -= Executor_FSMStateChanged;
                }
            }
            if (Context.IsPaused)
            {
                yield return Yield.Retry;
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

        protected override void Dispose(bool Disposing)
        {
            executor?.Dispose();
            base.Dispose(Disposing);
        }
    }
}

