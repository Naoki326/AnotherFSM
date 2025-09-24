using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class EngineBackendNode : BaseGroupNode
    {
        public FSMEngine? Backend => Engine;

    }
}
