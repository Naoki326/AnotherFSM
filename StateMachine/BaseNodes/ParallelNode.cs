using StateMachine.Interface;

namespace StateMachine
{
    public class FSMDescribe
    {
        public string StartNode { get; set; } = "";

        public string EndEvent { get; set; } = "";
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
                executor.NodeStateChanged += OnNodeStateChanged;
                executor.NodeExitChanged += OnNodeExitChanged;
            }
        }

        public ParallelNode()
        {
        }

        public ParallelNode(List<FSMDescribe> fsms)
        {
            this.FSMs = fsms;
        }

        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
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
                foreach (var executor in executors)
                {
                    await executor.RestartAsync(false);
                }
            }
            yield return Yield.None;
            executors.ForEach(p => p.FSMStateChanged += Executor_FSMStateChanged);
            using (Context.TokenSource.Token
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
                    await Task.WhenAny(Task.WhenAll(executors.Select(p => p.ExecutorTask)), Task.Delay(-1, Context.Token));
                    if (Context.IsPaused)
                    {
                        await Task.WhenAny(Task.WhenAll(executors.Select(p => p.CurrentNodeTask)), Task.Delay(-1, Context.Token));
                    }
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
    }
}
