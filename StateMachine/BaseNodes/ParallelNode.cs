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

        private List<FSMSingleThreadExecutor> executors = [];

        [FSMProperty("Parrllel FSM", true, 3)]
        public List<FSMDescribe> FSMs { get; set; } = [];

        public override void InitBeforeStart()
        {
            if (Engine is not null)
            {
                executors.ForEach(p => p.Dispose());
                executors.Clear();
                foreach (var fsm in FSMs)
                {
                    var executor = new FSMSingleThreadExecutor(Engine[fsm.StartNode], Engine.GetEvent(fsm.EndEvent));
                    executors.Add(executor);
                    executor.NodeStateChanged += OnNodeStateChanged;
                    executor.NodeExitChanged += OnNodeExitChanged;
                }
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
            if(executors.Count == 0)
            {
                PublishEvent(FSMEnum.Error);
                yield break;
            }
        Begin:
            if (executors.Any(p => p.State == FSMNodeState.Paused))
            {
                executors.ForEach(p =>
                {
                    if (!p.ExecutorTask.IsCompleted)
                    { p.Continue(); }
                });
            }
            else
            {
                executors.ForEach(async p => await p.RestartAsync());
            }
            yield return null;
            using (Context.TokenSource.Token.Register(() => executors.ForEach(p =>
            {
                if (!p.ExecutorTask.IsCompleted)
                { p.Pause(); }
            })))
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
                {
                }
            }
            if (Context.IsPaused)
            {
                yield return null;
                goto Begin;
            }
            yield return null;
            if (executors.Any(p => p.State == FSMNodeState.Stoped))
            {
                PublishEvent(FSMEnum.Cancel);
            }
            else
            {
                PublishEvent(FSMEnum.Next);
            }
        }

        protected override void Dispose(bool Disposing)
        {
            executors.ForEach(p => p.Dispose());
            base.Dispose(Disposing);
        }
    }
}
