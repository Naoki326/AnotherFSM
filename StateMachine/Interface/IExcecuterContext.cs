namespace StateMachine
{
    public interface IExecuterContext
    {
        int Index { get; set; }

        bool Condition { get; set; }

        string LastNodeName { get; internal set; }

        string CurrentNodeName { get; internal set; }

        HashSet<long> PauseAnchors { get; }
    }
}
