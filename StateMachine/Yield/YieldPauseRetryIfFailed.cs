using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldPauseRetryIfFailed : IYieldAction
    {
        public YieldEnum Result { get; set; } = YieldEnum.None;

        public bool IsMoveNext { get; set; } = true;

        public FSMNodeContext Context { set { } }

        public IExecuterContext SolveContext { set { } }

        private Func<Task> retryTask;

        public YieldPauseRetryIfFailed(Func<Task> retryTask)
        {
            this.retryTask = retryTask;
        }

        public Task AfterYieldAsync()
        {
            return Task.CompletedTask;
        }

        public async Task BeforeNextAsync()
        {
            try
            {
                await retryTask();
                IsMoveNext = true;
                Result = YieldEnum.None;
            }
            catch (Exception)
            {
                IsMoveNext = false;
                Result = YieldEnum.Pause;
            }
        }
    }
}
