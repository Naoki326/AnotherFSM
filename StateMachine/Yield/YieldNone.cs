using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldNone : IYieldAction
    {
        public YieldEnum Result => YieldEnum.None;

        public bool IsMoveNext => true;

        public FSMNodeContext Context { set { } }

        public IExcecuterContext SolveContext { set { } }

        public Task AfterYieldAsync()
        {
            return Task.CompletedTask;
        }

        public Task BeforeNextAsync()
        {
            return Task.CompletedTask;
        }
    }
}
