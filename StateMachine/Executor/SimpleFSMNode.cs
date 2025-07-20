using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class SimpleFSMNode : AbstractFSMNode, IFSMNode
    {

        //启动时触发
        private protected override Task RestartAsync()
        {
            return Task.CompletedTask;
        }
    }

    [DebuggerNonUserCode]
    public abstract class SimpleFSMNode<T> : AbstractFSMNode<T>, IFSMNode where T : class
    {

        //启动时触发
        private protected override Task RestartAsync()
        {
            return Task.CompletedTask;
        }
    }

}
