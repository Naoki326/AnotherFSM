using System.Collections;
using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class EnumFSMNode : AbstractFSMNode
    {
        protected abstract IEnumerable ExecuteEnumerable();

        private protected sealed override Task RestartAsync()
        {
            (executor as IEnumerator<object>)?.Dispose();
            executor = ExecuteEnumerable().GetEnumerator();
            return Task.CompletedTask;
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
                        if (!executor.MoveNext())
                        {
                            break;
                        }
                    }
                    if (executor.Current is IYieldAction yieldAfter)
                    {
                        yieldAfter.Context = context;
                        yieldAfter.SolveContext = ExecuterContext;
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

        private IEnumerator? executor;

        protected override void Dispose(bool disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(disposing);
        }
    }

    [DebuggerNonUserCode]
    public abstract class EnumFSMNode<T> : AbstractFSMNode<T> where T : class
    {
        protected abstract IEnumerable ExecuteEnumerable();

        private protected sealed override Task RestartAsync()
        {
            (executor as IEnumerator<object>)?.Dispose();
            executor = ExecuteEnumerable().GetEnumerator();
            return Task.CompletedTask;
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
                        if (!executor.MoveNext())
                        {
                            break;
                        }
                    }
                    if (executor.Current is IYieldAction yieldAfter)
                    {
                        yieldAfter.Context = context;
                        yieldAfter.SolveContext = ExecuterContext;
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
                context.CheckPause();
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