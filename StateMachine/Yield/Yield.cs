namespace StateMachine
{
    public static class Yield
    {
        /// <summary>
        /// 延时
        /// 如果延时过程中接收暂停，则终止延时
        /// </summary>
        /// <param name="sleepTime">延时时长</param>
        /// <returns></returns>
        public static IYieldAction Delay(int sleepTime)
        {
            return new YieldDelay(sleepTime);
        }

        /// <summary>
        /// 延时
        /// 如果延时过程中接收暂停，则终止延时
        /// </summary>
        /// <param name="sleepTime">延时时长</param>
        /// <returns></returns>
        public static IYieldAction Delay(TimeSpan sleepTime)
        {
            return new YieldDelay(sleepTime);
        }

        /// <summary>
        /// 插入暂停Anchor
        /// 执行器检测到该Anchor在需要被暂停的Anchor中，则暂停
        /// </summary>
        /// <param name="p">暂停Anchor</param>
        /// <returns></returns>
        public static IYieldAction Priority(Enum p)
        {
            return new YieldPriority(p);
        }

        /// <summary>
        /// 插入暂停Anchor
        /// 执行器检测到该Anchor在需要被暂停的Anchor中，则暂停
        /// </summary>
        /// <param name="p">暂停Anchor</param>
        /// <returns></returns>
        public static IYieldAction Priority(long p)
        {
            return new YieldPriority(p);
        }

        /// <summary>
        /// 发出当前状态中定义的可发送事件
        /// 对应的事件由当前状态的分支字典定义，需要一个index参数
        /// 该事件可以携带一个上下文:eventContext
        /// </summary>
        /// <param name="eventIndex">该参数定义发出的事件在分支字典中的索引</param>
        /// <param name="eventContext">事件携带的上下文</param>
        /// <returns></returns>
        public static IYieldAction Event(int eventIndex, object? eventContext = null)
        {
            return new YieldEvent(eventIndex, eventContext);
        }

        /// <summary>
        /// 发出当前状态中定义的可发送事件
        /// 对应的事件由当前状态的分支字典定义，需要一个index参数
        /// 该事件可以携带一个上下文:eventContext
        /// </summary>
        /// <param name="eventIndex">该参数定义发出的事件在分支字典中的索引（转化成int）</param>
        /// <param name="eventContext">事件携带的上下文</param>
        /// <returns></returns>
        public static IYieldAction Event(FSMEnum eventIndex, object? eventContext = null)
        {
            return new YieldEvent(eventIndex, eventContext);
        }


        /// <summary>
        /// 发出当前状态中定义的可发送事件
        /// 对应的事件由当前状态的分支字典定义，需要一个index参数
        /// 该事件可以携带一个上下文:eventContext
        /// </summary>
        /// <param name="eventIndex">该参数定义发出的事件在分支字典中的索引（转化成int）</param>
        /// <param name="eventContext">事件携带的上下文</param>
        /// <returns></returns>
        public static IYieldAction Event(Enum eventIndex, object? eventContext = null)
        {
            return new YieldEvent(eventIndex, eventContext);
        }

        private static IYieldAction next = new YieldEvent(FSMEnum.Next);
        // 发出Next事件，index：1
        public static IYieldAction Next => next;

        private static IYieldAction error = new YieldEvent(FSMEnum.Error);
        // 发出Error事件，index：-1
        public static IYieldAction Error => error;

        private static IYieldAction failed = new YieldEvent(FSMEnum.Failed);
        // 发出Failed事件，index：2
        public static IYieldAction Failed => failed;

        private static IYieldAction _break = new YieldEvent(FSMEnum.Break);
        // 发出Failed事件，index：3
        public static IYieldAction Break => _break;

        /// <summary>
        /// 插入一个try...catch块
        /// </summary>
        /// <param name="doTask">try块</param>
        /// <param name="doWhenException">catch块</param>
        /// <returns></returns>
        public static IYieldAction TryCatch(Func<Task> doTask, Action<Exception> doWhenException)
        {
            return new YieldTryCatch(doTask, doWhenException);
        }

        /// <summary>
        /// 插入一个try...catch...finally块
        /// </summary>
        /// <param name="doTask">try块</param>
        /// <param name="doWhenException">catch块</param>
        /// <param name="doFinally">finally块</param>
        /// <returns></returns>
        public static IYieldAction TryCatchFinally(Func<Task> doTask, Action<Exception> doWhenException, Action doFinally)
        {
            return new YieldTryCatch(doTask, doWhenException, doFinally);
        }

        /// <summary>
        /// 插入一个try...catch...finally块
        /// </summary>
        /// <param name="doTask">try块</param>
        /// <param name="doWhenException">catch块</param>
        /// <param name="doFinally">finally块</param>
        /// <returns></returns>
        public static IYieldAction TryCatchFinally(Func<Task> doTask, Action<Exception> doWhenException, Func<Task> doFinally)
        {
            return new YieldTryCatch(doTask, doWhenException, doFinally);
        }

        /// <summary>
        /// 通过枚举控制当前执行的Node
        /// </summary>
        /// <param name="ye">枚举</param>
        /// <returns></returns>
        public static IYieldAction Result(YieldEnum ye)
        {
            return new YieldResult(ye);
        }

        /// <summary>
        /// 如果在当前位置被暂停，下次恢复时自动执行restore
        /// 未暂停则无作用
        /// </summary>
        /// <param name="restore">暂停后恢复时，自动执行restore</param>
        /// <returns></returns>
        public static IYieldAction RestoreIfPause(Func<Task> restore)
        {
            return new YieldRestoreIfPaused(restore);
        }

        /// <summary>
        /// 如果失败，重试，直到未抛异常
        /// </summary>
        /// <param name="retryFunc">执行的方法</param>
        /// <returns></returns>
        public static IYieldAction RetryIfFailed(Func<Task> retryFunc)
        {
            return new YieldRetryIfFailed(retryFunc);
        }

        /// <summary>
        /// 如果在当前方法执行期间暂停
        /// 继续时会重试当前方法
        /// </summary>
        /// <param name="retryFunc">执行的方法</param>
        /// <returns></returns>
        public static IYieldAction RetryIfPaused(Func<Task> retryFunc)
        {
            return new YieldRetryIfPaused(retryFunc);
        }

        /// <summary>
        /// 如果失败，从当前状态的开头执行
        /// </summary>
        /// <param name="doFunc">执行的方法</param>
        /// <returns></returns>
        public static IYieldAction RestartIfFailed(Func<Task> doFunc)
        {
            return new YieldRestartIfFailed(doFunc);
        }

        /// <summary>
        /// 如果失败，暂停
        /// 继续时会重试当前方法
        /// </summary>
        /// <param name="retryFunc">执行的方法</param>
        /// <returns></returns>
        public static IYieldAction PauseIfFailed(Func<Task> retryFunc)
        {
            return new YieldPauseRetryIfFailed(retryFunc);
        }

        //插入可暂停的桩，执行器调用暂停时可在任意插桩位置暂停
        public static IYieldAction None { get; } = new YieldNone();

        //主动发出暂停当前执行器的动作
        public static IYieldAction Pause { get; } = new YieldPause();

        //回到当前Node的起点
        public static IYieldAction ToNodeStart { get; } = new YieldToNodeStart();

        //暂停，下次执行回到当前Node的起点
        public static IYieldAction PauseToNodeStart { get; } = new YieldPauseToNodeStart();
    }
}
