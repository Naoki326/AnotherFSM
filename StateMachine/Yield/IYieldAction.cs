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

        bool IsMoveNext { get; }

        FSMNodeContext Context { set; }

        // 当前Yield之后调用
        Task AfterYieldAsync();

        // 下次MoveNext之前调用
        Task BeforeNextAsync();
    }
}
