using System.Collections;

namespace StateMachine
{
    public abstract class EnumFSMNode : SimpleFSMNode
    {
        protected abstract IEnumerable ExecuteEnumerable();

        protected sealed override Task RestartAsync()
        {
            (executor as IEnumerator<object>)?.Dispose();
            executor = ExecuteEnumerable().GetEnumerator();
            return Task.CompletedTask;
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
                executor = ExecuteEnumerable().GetEnumerator();
            }
            while (true)
            {
                Context.CheckPause();
                try
                {
                    if (!executor.MoveNext())
                    {
                        (executor as IEnumerator<object>)?.Dispose();
                        executor = default;
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
                    try
                    {
                        if (!HandleException(e))
                        {
                            throw new Exception($"Node {(this as IFSMNode).Name} 存在异常未处理", e);
                        }
                        (executor as IEnumerator<object>)?.Dispose();
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

        private IEnumerator? executor;

        protected override void Dispose(bool Disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(Disposing);
        }
    }

    public abstract class EnumFSMNode<T> : SimpleFSMNode<T> where T : class
    {
        protected abstract IEnumerable ExecuteEnumerable();

        protected sealed override Task RestartAsync()
        {
            (executor as IEnumerator<object>)?.Dispose();
            executor = ExecuteEnumerable().GetEnumerator();
            return Task.CompletedTask;
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
                executor = ExecuteEnumerable().GetEnumerator();
            }
            while (true)
            {
                try
                {
                    if (!executor.MoveNext())
                    {
                        executor = ExecuteEnumerable().GetEnumerator();
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
                    try
                    {
                        if (!HandleException(e))
                        {
                            throw new Exception($"Node {(this as IFSMNode).Name} 存在异常未处理", e);
                        }
                        (executor as IEnumerator<object>)?.Dispose();
                        executor = default;
                        break;
                    }
                    catch (Exception e2)
                    {
                        throw new Exception($"Node {(this as IFSMNode).Name} 处理函数抛出异常！", e2);
                    }
                }
                Context?.CheckPause();
            }

        }

        private IEnumerator? executor;

        protected override void Dispose(bool Disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(Disposing);
        }
    }

    public abstract class EnumFSMNode<T, U> : SimpleFSMNode<T, U> where T : class where U : class
    {
        protected abstract IEnumerable ExecuteEnumerable();

        protected sealed override Task RestartAsync()
        {
            (executor as IEnumerator<object>)?.Dispose();
            executor = ExecuteEnumerable().GetEnumerator();
            return Task.CompletedTask;
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
                executor = ExecuteEnumerable().GetEnumerator();
            }
            while (true)
            {
                try
                {
                    if (!executor.MoveNext())
                    {
                        executor = ExecuteEnumerable().GetEnumerator();
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
                    try
                    {
                        if (!HandleException(e))
                        {
                            throw new Exception($"Node {(this as IFSMNode).Name} 存在异常未处理", e);
                        }
                        (executor as IEnumerator<object>)?.Dispose();
                        executor = default;
                        break;
                    }
                    catch (Exception e2)
                    {
                        throw new Exception($"Node {(this as IFSMNode).Name} 处理函数抛出异常！", e2);
                    }
                }
                Context.CheckPause();
            }

        }

        private IEnumerator? executor;

        protected override void Dispose(bool Disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(Disposing);
        }
    }
}