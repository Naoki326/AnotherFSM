using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldRestartIfFailed : IYieldAction
    {
        public YieldEnum Result { get; set; } = YieldEnum.None;

        public bool IsMoveNext { get; set; } = true;

        public FSMNodeContext Context { set { } }

        public IExecuterContext SolveContext { set { } }

        private Func<Task> doTask;

        public YieldRestartIfFailed(Func<Task> doTask)
        {
            this.doTask = doTask;
        }

        public Task AfterYieldAsync()
        {
            return Task.CompletedTask;
        }

        public async Task BeforeNextAsync()
        {
            try
            {
                await doTask();
                IsMoveNext = true;
                Result = YieldEnum.None;
            }
            catch (Exception)
            {
                IsMoveNext = false;
                Result = YieldEnum.ToNodeStart;
            }
        }
    }
}
