using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class SimpleFSMNode : AbstractFSMNode, IFSMNode
    {

        //启动时触发
        protected override Task RestartAsync()
        {
            return Task.CompletedTask;
        }

        //退出当前节点时触发
        protected override Task ExitAsync()
        {
            return Task.CompletedTask;
        }

        //暂停时的保存现场操作
        protected override Task Interupt()
        {
            return Task.CompletedTask;
        }
    }

    [DebuggerNonUserCode]
    public abstract class SimpleFSMNode<T> : AbstractFSMNode<T>, IFSMNode where T : class
    {

        //启动时触发
        protected override Task RestartAsync()
        {
            return Task.CompletedTask;
        }

        //退出当前节点时触发
        protected override Task ExitAsync()
        {
            return Task.CompletedTask;
        }

        //暂停时的保存现场操作
        protected override Task Interupt()
        {
            return Task.CompletedTask;
        }
    }

}
