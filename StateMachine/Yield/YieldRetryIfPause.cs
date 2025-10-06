using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldRetryIfPaused : IYieldAction
    {
        public YieldEnum Result => YieldEnum.None;

        public bool IsMoveNext { get; set; } = true;

        public FSMNodeContext Context { set { } }

        public IExcecuterContext SolveContext { set { } }

        private Func<Task> retryTask;

        public YieldRetryIfPaused(Func<Task> retryTask)
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
            catch (OperationCanceledException)
            {
                IsMoveNext = false;
            }
            catch (Exception ex) when (ex.InnerException is OperationCanceledException)
            {
                IsMoveNext = false;
            }
        }
    }
}
