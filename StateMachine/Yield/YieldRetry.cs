using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldRetry : IYieldAction
    {
        public YieldEnum Result => YieldEnum.Retry;

        public FSMNodeContext Context { set { } }

        public Task InvokeAsync()
        {
            return Task.CompletedTask;
        }

        public Task RestoreAsync()
        {
            return Task.CompletedTask;
        }
    }
}
