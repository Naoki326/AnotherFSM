using StateMachine;
using StateMachine.Interface;

namespace StateMachine
{
    [FSMNode("Group", "流程包装节点", [1, 5], ["NextEvent", "CancelEvent"], Id = 3)]
    public class GroupNode : BaseGroupNode
    {

        private FSMExecutor executor;

        [FSMProperty("Start node's name", true, 3)]
        public string StartName { get; set; }

        [FSMProperty("End event's name", true, 4)]
        public string EndEvent { get; set; }

        public override void InitBeforeStart()
        {
            executor?.Dispose();
            executor = new FSMExecutor(Engine[StartName], Engine.GetEvent(EndEvent));
            executor.NodeStateChanged += OnNodeStateChanged;
            executor.NodeExitChanged += OnNodeExitChanged;
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
        Begin:
            yield return null;
            if (executor.State == FSMNodeState.Paused)
            {
                executor.Continue();
            }
            else
            {
                await executor.RestartAsync();
            }
            yield return null;
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
                    executor.FSMStateChanged -= Executor_FSMStateChanged;
                }
            }
            executor.FSMStateChanged -= Executor_FSMStateChanged;
            if (Context.IsPaused)
            {
                yield return null;
                goto Begin;
            }
            yield return null;
            if (executor.State == FSMNodeState.Finished)
            {
                PublishEvent(FSMEnum.Next);
            }
            else if (executor.State == FSMNodeState.Stoped)
            {
                PublishEvent(FSMEnum.Cancel);
            }
        }

        private void Executor_FSMStateChanged(FSMExecutor arg1, FSMNodeState arg2, FSMNodeState arg3)
        {
            if (arg2 == FSMNodeState.Paused)
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

