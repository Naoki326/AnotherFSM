namespace StateMachine
{
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

        private async Task<YieldEnum> CheckYield(IYieldAction? current)
        {
            if (current is null)
            {
                return YieldEnum.None;
            }
            current.Context = Context;
            await current.InvokeAsync();
            return current.Result;
        }

        protected override async Task ExecuteMethodAsync()
        {
            if (executor is null)
            {
                executor = ExecuteEnumerable().GetAsyncEnumerator();
            }
            while (true)
            {
                Context.CheckPause();
                try
                {
                    if (!await executor.MoveNextAsync())
                    {
                        await executor.DisposeAsync();
                        executor = default!;
                        break;
                    }
                    switch (await CheckYield((IYieldAction?)executor.Current))
                    {
                        case YieldEnum.PauseRetry:
                            Pause();
                            await RestartAsync();
                            break;
                        case YieldEnum.Pause:
                            Pause();
                            break;
                        case YieldEnum.Retry:
                            break;
                        case YieldEnum.None:
                        default:
                            break;
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

        protected override void Dispose(bool Disposing)
        {
            if (executor != null)
                executor.DisposeAsync();
            base.Dispose(Disposing);
        }
    }

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

        private async Task<YieldEnum> CheckYield(IYieldAction? current)
        {
            if (current is null)
            {
                return YieldEnum.None;
            }
            current.Context = Context;
            await current.InvokeAsync();
            return current.Result;
        }

        protected override async Task ExecuteMethodAsync()
        {
            if (executor == null)
            {
                executor = ExecuteEnumerable().GetAsyncEnumerator();
            }
            while (true)
            {
                try
                {
                    if (!await executor.MoveNextAsync())
                    {
                        executor = ExecuteEnumerable().GetAsyncEnumerator();
                        break;
                    }
                    switch (await CheckYield((IYieldAction?)executor.Current))
                    {
                        case YieldEnum.PauseRetry:
                            Pause();
                            await RestartAsync();
                            break;
                        case YieldEnum.Pause:
                            Pause();
                            break;
                        case YieldEnum.Retry:
                            await RestartAsync();
                            break;
                        case YieldEnum.None:
                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    if (!HandleException(e))
                    {
                        throw new Exception($"Node {(this as IFSMNode).Name} 存在异常未处理", e);
                    }
                    await executor.DisposeAsync();
                    executor = default;
                    break;
                }
                Context?.CheckPause();
            }
        }

        private IAsyncEnumerator<object>? executor;

        protected override void Dispose(bool Disposing)
        {
            if (executor != null)
                executor.DisposeAsync();
            base.Dispose(Disposing);
        }
    }

    public abstract class AsyncEnumFSMNode<T, U> : SimpleFSMNode<T, U> where T : class where U : class
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
        private async Task<YieldEnum> CheckYield(IYieldAction? current)
        {
            if (current is null)
            {
                return YieldEnum.None;
            }
            current.Context = Context;
            await current.InvokeAsync();
            return current.Result;
        }

        protected override async Task ExecuteMethodAsync()
        {
            if (executor == null)
            {
                executor = ExecuteEnumerable().GetAsyncEnumerator();
            }
            while (true)
            {
                try
                {
                    if (!await executor.MoveNextAsync())
                    {
                        executor = ExecuteEnumerable().GetAsyncEnumerator();
                        break;
                    }
                    switch (await CheckYield((IYieldAction?)executor.Current))
                    {
                        case YieldEnum.PauseRetry:
                            Pause();
                            await RestartAsync();
                            break;
                        case YieldEnum.Pause:
                            Pause();
                            break;
                        case YieldEnum.Retry:
                            await RestartAsync();
                            break;
                        case YieldEnum.None:
                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    if (!HandleException(e))
                    {
                        throw new Exception($"Node {(this as IFSMNode).Name} 存在异常未处理", e);
                    }
                    await executor.DisposeAsync();
                    executor = default;
                    break;
                }
                Context.CheckPause();
            }
        }

        private IAsyncEnumerator<object>? executor;

        protected override void Dispose(bool Disposing)
        {
            if (executor != null)
                executor.DisposeAsync();
            base.Dispose(Disposing);
        }
    }
}
