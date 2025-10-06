using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldRetryIfFailed : IYieldAction
    {
        public YieldEnum Result => YieldEnum.None;

        public bool IsMoveNext { get; set; } = true;

        public FSMNodeContext Context { set { } }

        public IExecuterContext SolveContext { set { } }

        private Func<Task> retryTask;

        public YieldRetryIfFailed(Func<Task> retryTask)
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
            }
            catch (Exception)
            {
                IsMoveNext = false;
            }
        }
    }
}
