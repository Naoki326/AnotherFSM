using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldPauseToNodeStart : IYieldAction
    {
        public YieldEnum Result => YieldEnum.PauseToNodeStart;

        public bool IsMoveNext { get; set; } = true;

        public FSMNodeContext Context { set { } }

        public IExecuterContext SolveContext { set { } }

        public YieldPauseToNodeStart()
        {
        }

        public Task AfterYieldAsync()
        {
            return Task.CompletedTask;
        }

        public Task BeforeNextAsync()
        {
            return Task.CompletedTask;
        }
    }
}
