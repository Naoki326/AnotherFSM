namespace StateMachine
{
    public enum YieldEnum
    {
        None,
        Pause,
        ToNodeStart,
        PauseToNodeStart,
    }

    public interface IYieldAction
    {
        YieldEnum Result { get; }

        // 为重试机制设置
        bool IsMoveNext { get; }

        FSMNodeContext Context { set; }

        IExecuterContext SolveContext { set; }

        // 当前Yield之后调用
        Task AfterYieldAsync();

        // 下次MoveNext之前调用
        Task BeforeNextAsync();
    }
}
