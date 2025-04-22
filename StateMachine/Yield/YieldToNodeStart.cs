using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldToNodeStart : IYieldAction
    {
        public YieldEnum Result => YieldEnum.ToNodeStart;

        public bool IsMoveNext { get; set; } = true;

        public FSMNodeContext Context { set { } }

        public YieldToNodeStart()
        {
        }

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
