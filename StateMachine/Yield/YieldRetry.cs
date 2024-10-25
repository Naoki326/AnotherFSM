namespace StateMachine
{
    internal class YieldRetry : IYieldAction
    {
        public YieldEnum Result => YieldEnum.Retry;

        public FSMNodeContext Context { set { } }

        public Task InvokeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
