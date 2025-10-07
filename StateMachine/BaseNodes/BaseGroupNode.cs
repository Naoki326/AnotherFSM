using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class BaseGroupNode : AsyncEnumFSMNode
    {

        internal event EventHandler<string>? NodeStateChanged;
        protected void OnNodeStateChanged(object sender, string name)
        {
            NodeStateChanged?.Invoke(sender, name);
        }

        internal event EventHandler<string>? NodeExitChanged;
        protected void OnNodeExitChanged(object sender, string name)
        {
            NodeExitChanged?.Invoke(sender, name);
        }

    }

    [DebuggerNonUserCode]
    public abstract class BaseGroupNode<T> : AsyncEnumFSMNode<T> where T : class
    {

        internal event EventHandler<string>? NodeStateChanged;
        protected void OnNodeStateChanged(object sender, string name)
        {
            NodeStateChanged?.Invoke(sender, name);
        }

        internal event EventHandler<string>? NodeExitChanged;
        protected void OnNodeExitChanged(object sender, string name)
        {
            NodeExitChanged?.Invoke(sender, name);
        }

    }
}
