namespace StateMachine
{
    public static class Yield
    {
        public static IYieldAction Priority(Enum p)
        {
            return new YieldPriority(p);
        }

        public static IYieldAction Priority(long p)
        {
            return new YieldPriority(p);
        }

        public static IYieldAction Event(int eventIndex)
        {
            return new YieldEvent(eventIndex);
        }

        public static IYieldAction Event(FSMEnum eventIndex)
        {
            return new YieldEvent(eventIndex);
        }

        public static IYieldAction Result(YieldEnum ye)
        {
            return new YieldResult(ye);
        }

        public static IYieldAction RestoreIfPause(Func<Task> restore)
        {
            return new YieldRestoreIfPause(restore);
        }

        public static IYieldAction RetryIfFailed(Func<Task> retryFunc)
        {
            return new YieldRetryIfFailed(retryFunc);
        }

        public static IYieldAction RestartIfFailed(Func<Task> doFunc)
        {
            return new YieldRestartIfFailed(doFunc);
        }

        public static IYieldAction PauseIfFailed(Func<Task> retryFunc)
        {
            return new YieldPauseRetryIfFailed(retryFunc);
        }

        public static IYieldAction None { get; } = new YieldNone();

        public static IYieldAction Pause { get; } = new YieldPause();

        public static IYieldAction ToNodeStart { get; } = new YieldToNodeStart();

        public static IYieldAction PauseToNodeStart { get; } = new YieldPauseToNodeStart();
    }
}
