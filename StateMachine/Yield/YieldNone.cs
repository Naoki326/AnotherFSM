using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldNone : IYieldAction
    {
        public YieldEnum Result => YieldEnum.None;

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
