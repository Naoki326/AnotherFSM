using System.Collections;
using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
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
                        if (!executor.MoveNext())
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

        protected override void Dispose(bool disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(disposing);
        }
    }

    [DebuggerNonUserCode]
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
        protected override async Task ExecuteMethodAsync()
        {
            bool isMoveNext = true;
            while (true)
            {
                try
                {
                    if (executor.Current is IYieldAction yieldBefore)
                    {
                        await yieldBefore.BeforeNextAsync();
                        isMoveNext = yieldBefore.IsMoveNext;
                    }
                    if (isMoveNext)
                    {
                        if (!executor.MoveNext())
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

        protected override void Dispose(bool disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(disposing);
        }
    }

}