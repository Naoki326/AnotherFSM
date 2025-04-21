using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public class YieldEvent : IYieldAction
    {
        private readonly int eventIndex;

        public YieldEvent(int eventIndex)
        {
            this.eventIndex = eventIndex;
        }

        public YieldEvent(FSMEnum eventEnum)
        {
            this.eventIndex = eventEnum.GetHashCode();
        }

        public YieldEnum Result => YieldEnum.None;

        public FSMNodeContext Context { set => throw new NotImplementedException(); }

        public Task InvokeAsync()
        {
            throw new NotImplementedException();
        }

        public Task RestoreAsync()
        {
            return Task.CompletedTask;
        }
    }
}
