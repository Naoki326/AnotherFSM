using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldNone : IYieldAction
    {
        public YieldEnum Result => YieldEnum.None;

        public bool IsMoveNext => true;

        public FSMExecutionControl Control { set { } }

        public IExecuterContext SolveContext { set { } }

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
