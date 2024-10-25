namespace StateMachine
{
    public class YieldResult : IYieldAction
    {
        private YieldEnum result;
        public YieldEnum Result => result;

        public FSMNodeContext Context { set { } }

        public Task InvokeAsync()
        {
            return Task.CompletedTask;
        }

        public YieldResult(YieldEnum result)
        {
            this.result = result;
        }

        public static explicit operator YieldResult(YieldEnum b) => new(b);
    }
}
