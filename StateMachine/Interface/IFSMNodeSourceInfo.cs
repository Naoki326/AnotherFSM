using System;
using System.Collections.Generic;
using System.Text;

namespace StateMachine
{
    public interface IFSMNodeSourceInfo
    {
        public string SourceCSPath { get; }

        public IReadOnlyList<int> Events { get; }
    }

}
