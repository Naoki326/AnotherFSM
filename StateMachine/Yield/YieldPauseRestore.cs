using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldPauseRestore : IYieldAction
    {
        private readonly Func<Task> restore;

        public YieldEnum Result => YieldEnum.Pause;

        public FSMNodeContext Context { set { } }

        public YieldPauseRestore(Func<Task> restore)
        {
            this.restore = restore;
        }

        public Task RestoreAsync()
        {
            return restore();
        }

        public Task InvokeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
