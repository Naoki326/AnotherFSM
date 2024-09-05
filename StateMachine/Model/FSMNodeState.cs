namespace StateMachine
{
    public enum FSMNodeState
    {
        Uninitialized = 0,
        Initialized = 1,
        Running = 2,
        Interrupted = 3,
        Pausing = 4,
        Paused = 5,
        Proceeding = 6,
        Continueing = 6,
        Stopping = 7,
        Stoped = 8,
        Finished = 9
    }
}
