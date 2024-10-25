namespace StateMachine
{
    internal class YieldPause : IYieldAction
    {
        public YieldEnum Result => YieldEnum.Pause;

        public FSMNodeContext Context { set { } }

        public Task InvokeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
