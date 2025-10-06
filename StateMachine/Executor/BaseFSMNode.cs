using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class BaseFSMNode : AbstractFSMNode, IFSMNode
    {

        //启动时触发
        private protected override Task RestartAsync()
        {
            return Task.CompletedTask;
        }

        private protected override async Task ExecuteMethodAsync()
        {
            await ExecuteAsync();
        }

        protected abstract Task ExecuteAsync();
    }

    [DebuggerNonUserCode]
    public abstract class BaseFSMNode<T> : AbstractFSMNode<T>, IFSMNode where T : class
    {

        //启动时触发
        private protected override Task RestartAsync()
        {
            return Task.CompletedTask;
        }
        private protected override async Task ExecuteMethodAsync()
        {
            await ExecuteAsync();
        }

        protected abstract Task ExecuteAsync();
    }

}
