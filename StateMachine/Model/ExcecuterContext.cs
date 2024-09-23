namespace StateMachine
{
    public class ExcecuterContext : IExcecuterContext
    {
        public int Index { get; set; } = 0;

        public bool Condition { get; set; } = false;

        public string LastNodeName { get; set; } = "";

        public string CurrentNodeName { get; set; } = "";

        string IExcecuterContext.LastNodeName => LastNodeName;
        string IExcecuterContext.CurrentNodeName => CurrentNodeName;
    }
}
