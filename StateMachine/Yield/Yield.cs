namespace StateMachine
{
    public static class Yield
    {
        public static IYieldAction PauseRetry { get; } = new YieldPauseRetry();

        public static IYieldAction Retry { get; } = new YieldRetry();

        public static IYieldAction Pause { get; } = new YieldPause();

        public static IYieldAction None { get; } = new YieldNone();
    }
}
