namespace StateMachine
{
    public abstract class BaseGroupNode : AsyncEnumFSMNode
    {

        internal void SetEngine(FSMEngine e)
        {
            Engine = e;
        }

        public FSMEngine Engine { get; private set; }


        public event EventHandler<string> NodeStateChanged;
        protected void OnNodeStateChanged(object sender, string name)
        {
            NodeStateChanged?.Invoke(sender, name);
        }

        public event EventHandler<string> NodeExitChanged;
        protected void OnNodeExitChanged(object sender, string name)
        {
            NodeExitChanged?.Invoke(sender, name);
        }

    }
}
