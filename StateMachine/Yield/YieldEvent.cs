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
        public int EventIndex => eventIndex;

        public object? EventContext { get; private set; }

        public IExecuterContext SolveContext { set { } }

        public YieldEvent(int eventIndex, object? eventContext = null)
        {
            this.eventIndex = eventIndex;
            EventContext = eventContext;
        }

        public YieldEvent(FSMEnum eventEnum, object? eventContext = null)
        {
            this.eventIndex = eventEnum.GetHashCode();
            EventContext = eventContext;
        }

        public YieldEvent(Enum eventEnum, object? eventContext = null)
        {
            this.eventIndex = eventEnum.GetHashCode();
            EventContext = eventContext;
        }

        public YieldEnum Result => YieldEnum.None;

        public bool IsMoveNext => true;

        public FSMNodeContext Context { set { } }

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
