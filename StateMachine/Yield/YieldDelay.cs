using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public class YieldDelay : IYieldAction
    {
        public YieldEnum Result => YieldEnum.None;

        private CancellationToken token = default;
        public FSMNodeContext Context { set => token = value.Token; }

        public IExecuterContext SolveContext { set { } }

        public bool IsMoveNext => true;

        public async Task AfterYieldAsync()
        {
            await Task.Delay(delayTime, token);
        }

        public Task BeforeNextAsync()
        {
            return Task.CompletedTask;
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
