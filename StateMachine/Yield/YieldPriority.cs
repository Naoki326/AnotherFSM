using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public class YieldPriority : IYieldAction
    {
        private YieldEnum result = YieldEnum.None;
        public YieldEnum Result => result;

        public bool IsMoveNext => true;

        private long contextPriority;
        public FSMNodeContext Context { set { contextPriority = value.ManualLevel; } }

        public Task AfterYieldAsync()
        {
            if (contextPriority > priority)
            {
                result = YieldEnum.Pause;
            }
            else
            {
                result = YieldEnum.None;
            }
            return Task.CompletedTask;
        }

        public Task BeforeNextAsync()
        {
            return Task.CompletedTask;
        }

        private long priority;
        public YieldPriority(long p)
        {
            priority = p;
        }
        public YieldPriority(Enum p)
        {
            priority = p.GetHashCode();
        }

        public static explicit operator YieldPriority(long b) => new(b);
        public static explicit operator YieldPriority(Enum b) => new(b);
    }
}
