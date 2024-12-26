namespace StateMachine
{
    public abstract class BaseGroupNode : AsyncEnumFSMNode
    {

        internal void SetEngine(FSMEngine e)
        {
            Engine = e;
        }

        internal FSMEngine? Engine { get; private set; }


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
