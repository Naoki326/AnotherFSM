using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class AsyncEnumFSMNode : AbstractFSMNode
    {
        protected abstract IAsyncEnumerable<object> ExecuteEnumerable();

        private protected sealed override async Task RestartAsync()
        {
            if (executor != null)
                await executor.DisposeAsync();
            executor = ExecuteEnumerable().GetAsyncEnumerator();
        }

        //针对yield不方便使用try-catch模块而设计
        protected virtual void HandleException(Exception e)
        {
        }

        private protected sealed override async Task ExecuteMethodAsync()
        {
            bool isMoveNext = true;
            while (true)
            {
                context.CheckPause();
                try
                {
                    isMoveNext = true;
                    if (executor.Current is IYieldAction yieldBefore)
                    {
                        await yieldBefore.BeforeNextAsync();
                        isMoveNext = yieldBefore.IsMoveNext;
                        if(yieldBefore is YieldEvent yEvent)
                        {
                            PublishEvent(yEvent.EventIndex);
                        }
                    }
                    if (isMoveNext)
                    {
                        if (!await executor.MoveNextAsync())
                        {
                            break;
                        }
                    }
                    if (executor.Current is IYieldAction yieldAfter)
                    {
                        yieldAfter.Context = context;
                        await yieldAfter.AfterYieldAsync();
                        switch (yieldAfter.Result)
                        {
                            case YieldEnum.Pause:
                                Pause();
                                break;
                            case YieldEnum.ToNodeStart:
                                await RestartAsync();
                                break;
                            case YieldEnum.PauseToNodeStart:
                                Pause();
                                await RestartAsync();
                                break;
                            case YieldEnum.None:
                            default:
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    HandleException(e);
                    throw e;
                }
            }
            context.CheckPause();
        }

        private IAsyncEnumerator<object>? executor;

        protected override void Dispose(bool disposing)
        {
            if (executor != null)
                executor.DisposeAsync().ConfigureAwait(false);
            base.Dispose(disposing);
        }
    }

    [DebuggerNonUserCode]
    public abstract class AsyncEnumFSMNode<T> : AbstractFSMNode<T> where T : class
    {
        protected abstract IAsyncEnumerable<object> ExecuteEnumerable();

        private protected sealed override async Task RestartAsync()
        {
            if (executor != null)
                await executor.DisposeAsync();
            executor = ExecuteEnumerable().GetAsyncEnumerator();
        }

        //针对yield不方便使用try-catch模块而设计
        protected virtual void HandleException(Exception e)
        {
        }

        private protected sealed override async Task ExecuteMethodAsync()
        {
            bool isMoveNext = true;
            while (true)
            {
                context.CheckPause();
                try
                {
                    if (executor.Current is IYieldAction yieldBefore)
                    {
                        await yieldBefore.BeforeNextAsync();
                        isMoveNext = yieldBefore.IsMoveNext;
                        if (yieldBefore is YieldEvent yEvent)
                        {
                            PublishEvent(yEvent.EventIndex);
                        }
                    }
                    if (isMoveNext)
                    {
                        if (!await executor.MoveNextAsync())
                        {
                            break;
                        }
                    }

                    if (executor.Current is IYieldAction yieldAfter)
                    {
                        yieldAfter.Context = context;
                        await yieldAfter.AfterYieldAsync();
                        switch (yieldAfter.Result)
                        {
                            case YieldEnum.Pause:
                                Pause();
                                break;
                            case YieldEnum.ToNodeStart:
                                await RestartAsync();
                                break;
                            case YieldEnum.PauseToNodeStart:
                                Pause();
                                await RestartAsync();
                                break;
                            case YieldEnum.None:
                            default:
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    HandleException(e);
                    throw e;
                }
            }
            context.CheckPause();
        }

        private IAsyncEnumerator<object>? executor;

        protected override void Dispose(bool disposing)
        {
            if (executor != null)
                executor.DisposeAsync().ConfigureAwait(false);
            base.Dispose(disposing);
        }
    }

}
