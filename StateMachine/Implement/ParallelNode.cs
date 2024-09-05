using StateMachine.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StateMachine.Implement
{
    public class FSMDescribe
    {
        public string StartNode { get; set; }

        public string EndEvent { get; set; }
    }

    [FSMNode("Parallel", "并行流程包装节点", [1, 5], ["NextEvent", "CancelEvent"], Id = 4)]
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
        Begin:
            if (executors.Any(p => p.State == FSMNodeState.Paused))
            {
                executors.ForEach(p => p.Continue());
            }
            else
            {
                executors.ForEach(p => p.Restart());
            }
            yield return null;
            using (Context.TokenSource.Token.Register(() => executors.ForEach(p => p.Pause())))
            {
                try
                {
                    await Task.WhenAny(Task.WhenAll(executors.Select(p => p.ExecutorTask)), Task.Delay(-1, Context.Token));
                    if (Context.IsPaused)
                    {
                        Task.WaitAll(executors.Select(p => p.CurrentNodeTask).ToArray());
                    }
                }
                catch (OperationCanceledException ex)
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
