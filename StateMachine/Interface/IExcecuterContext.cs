namespace StateMachine
{
    public interface IExecuterContext
    {
        int Index { get; set; }

        bool Condition { get; set; }

        string LastNodeName { get; }

        string CurrentNodeName { get; }

        HashSet<long> PauseAnchors { get; }
    }
}
