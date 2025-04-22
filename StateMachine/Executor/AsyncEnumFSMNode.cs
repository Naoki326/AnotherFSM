using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class AsyncEnumFSMNode : SimpleFSMNode
    {
        protected abstract IAsyncEnumerable<object> ExecuteEnumerable();

        protected sealed override async Task RestartAsync()
        {
            if (executor != null)
                await executor.DisposeAsync();
            executor = ExecuteEnumerable().GetAsyncEnumerator();
        }

        //针对yield不方便使用try-catch模块而设计
        protected virtual bool HandleException(Exception e)
        {
            return false;
        }

        protected override async Task ExecuteMethodAsync()
        {
            bool isMoveNext = true;
            while (true)
            {
                Context.CheckPause();
                try
                {
                    isMoveNext = true;
                    if (executor.Current is IYieldAction yieldBefore)
                    {
                        await yieldBefore.BeforeNextAsync();
                        isMoveNext = yieldBefore.IsMoveNext;
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
                        yieldAfter.Context = Context;
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
                    try
                    {
                        if (!HandleException(e))
                        {
                            throw new Exception($"Node {(this as IFSMNode).Name} 存在异常未处理", e);
                        }
                        await executor.DisposeAsync();
                        executor = default;
                        break;
                    }
                    catch (Exception e2)
                    {
                        throw new Exception($"Node {(this as IFSMNode).Name} 处理函数抛出异常！", e2);
                    }
                }
            }
            Context.CheckPause();
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
    public abstract class AsyncEnumFSMNode<T> : SimpleFSMNode<T> where T : class
    {
        protected abstract IAsyncEnumerable<object> ExecuteEnumerable();

        protected sealed override async Task RestartAsync()
        {
            if (executor != null)
                await executor.DisposeAsync();
            executor = ExecuteEnumerable().GetAsyncEnumerator();
        }

        //针对yield不方便使用try-catch模块而设计
        protected virtual bool HandleException(Exception e)
        {
            return false;
        }

        protected override async Task ExecuteMethodAsync()
        {
            bool isMoveNext = true;
            while (true)
            {
                Context.CheckPause();
                try
                {
                    if (executor.Current is IYieldAction yieldBefore)
                    {
                        await yieldBefore.BeforeNextAsync();
                        isMoveNext = yieldBefore.IsMoveNext;
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
                        yieldAfter.Context = Context;
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
                    try
                    {
                        if (!HandleException(e))
                        {
                            throw new Exception($"Node {(this as IFSMNode).Name} 存在异常未处理", e);
                        }
                        await executor.DisposeAsync();
                        executor = default;
                        break;
                    }
                    catch (Exception e2)
                    {
                        throw new Exception($"Node {(this as IFSMNode).Name} 处理函数抛出异常！", e2);
                    }
                }
            }
            Context.CheckPause();
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
