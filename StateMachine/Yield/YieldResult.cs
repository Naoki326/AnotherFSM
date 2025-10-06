using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public class YieldResult : IYieldAction
    {
        private YieldEnum result;
        public YieldEnum Result => result;

        public bool IsMoveNext => true;

        public FSMNodeContext Context { set { } }

        public IExecuterContext SolveContext { set { } }

        public Task AfterYieldAsync()
        {
            return Task.CompletedTask;
        }

        public Task BeforeNextAsync()
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
