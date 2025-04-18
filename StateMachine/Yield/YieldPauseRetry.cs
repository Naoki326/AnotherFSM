using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldPauseRetry : IYieldAction
    {
        public YieldEnum Result => YieldEnum.PauseRetry;

        public FSMNodeContext Context { set { } }

        public Task InvokeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
