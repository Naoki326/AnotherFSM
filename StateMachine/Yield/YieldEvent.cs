using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public class YieldEvent : IYieldAction
    {
        public YieldEnum Result => throw new NotImplementedException();

        public FSMNodeContext Context { set => throw new NotImplementedException(); }

        public Task InvokeAsync()
        {
            throw new NotImplementedException();
        }
    }
}
