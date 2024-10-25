namespace StateMachine
{
    public class YieldDelay : IYieldAction
    {
        public YieldEnum Result => YieldEnum.None;

        private CancellationToken token = default;
        public FSMNodeContext Context { set => token = value.Token; }

        public async Task InvokeAsync()
        {
            await Task.Delay(delayTime, token);
        }

        private int delayTime = -1;
        public YieldDelay(int dt)
        {
            delayTime = dt;
        }
        public YieldDelay(TimeSpan timeSpan)
        {
            new YieldDelay(TimeSpan.FromSeconds(timeSpan.TotalSeconds));
            delayTime = timeSpan.Milliseconds;
        }

        public static explicit operator YieldDelay(int dt) => new(dt);
    }
}
