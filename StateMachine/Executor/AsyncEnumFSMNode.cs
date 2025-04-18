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

        /// <summary>
        /// 重试一个操作
        /// </summary>
        /// <param name="action">被重试的操作</param>
        /// <param name="count">重试的次数，若小于0则表示不停重试</param>
        /// <returns>若重试，返回重试的index</returns>
        /// <exception cref="IndexOutOfRangeException">超过重试次数</exception>
        protected async IAsyncEnumerable<int> RetryAsync(Task action, int count = -1)
        {
            int times = 0;
            while (true)
            {
                if(count > 0 && times >= count)
                {
                    throw new IndexOutOfRangeException("重试次数超出限制");
                }
                yield return times;

                times++;

                try
                {
                    await action;

                    // 如果成功完成操作，则退出循环
                    break;
                }
                catch (OperationCanceledException)
                {
                    // 如果被取消（暂停），则继续循环
                    continue;
                }
            }
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
                        case YieldEnum.Pause:
                            Pause();
                            break;
                        case YieldEnum.Retry:
                            await RestartAsync();
                            break;
                        case YieldEnum.PauseRetry:
                            Pause();
                            await RestartAsync();
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

        protected override void Dispose(bool disposing)
        {
            if (executor != null)
                executor.DisposeAsync();
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


        /// <summary>
        /// 重试一个操作
        /// </summary>
        /// <param name="action">被重试的操作</param>
        /// <param name="count">重试的次数，若小于0则表示不停重试</param>
        /// <returns>若重试，返回重试的index</returns>
        /// <exception cref="IndexOutOfRangeException">超过重试次数</exception>
        protected async IAsyncEnumerable<int> RetryAsync(Task action, int count = -1)
        {
            int times = 0;
            while (true)
            {
                if (count > 0 && times >= count)
                {
                    throw new IndexOutOfRangeException("重试次数超出限制");
                }
                yield return times;

                times++;

                try
                {
                    await action;

                    // 如果成功完成操作，则退出循环
                    break;
                }
                catch (OperationCanceledException)
                {
                    // 如果被取消（暂停），则继续循环
                    continue;
                }
            }
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

        protected override void Dispose(bool disposing)
        {
            if (executor != null)
                executor.DisposeAsync();
            base.Dispose(disposing);
        }
    }

}
