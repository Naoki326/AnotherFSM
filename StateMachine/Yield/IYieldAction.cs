namespace StateMachine
{
    public enum YieldEnum
    {
        None,
        Pause,
        Retry,
        PauseRetry,
    }

    public interface IYieldAction
    {
        YieldEnum Result { get; }

        FSMNodeContext Context { set; }

        Task InvokeAsync();

        Task RestoreAsync();
    }
}
