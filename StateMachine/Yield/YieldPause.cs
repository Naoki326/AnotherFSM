using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldPause : IYieldAction
    {
        public YieldEnum Result => YieldEnum.Pause;

        public bool IsMoveNext => true;

        public FSMNodeContext Context { set { } }

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
