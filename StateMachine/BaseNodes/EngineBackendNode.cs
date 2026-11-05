using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class EngineBackendNode : AsyncEnumFSMNode
    {
        public FSMEngine? Backend => Engine;

    }
}
