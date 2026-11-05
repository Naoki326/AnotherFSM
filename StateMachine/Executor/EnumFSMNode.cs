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
                try
                {
                    if (executor.Current is IYieldAction yieldBefore)
                    {
                        await yieldBefore.BeforeNextAsync();
                        isMoveNext = yieldBefore.IsMoveNext;
                        if (yieldBefore is YieldEvent yEvent)
                        {
                            if (yEvent.EventContext is null)
                            { PublishEvent(yEvent.EventIndex); }
                            else
                            { PublishEvent(yEvent.EventIndex, yEvent.EventContext); }
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
                        yieldAfter.Control = Control;
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
                Control.CheckPause();
            }
            Control.CheckPause();
        }

        private IEnumerator? executor;

        protected override void Dispose(bool disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(disposing);
        }

        internal override Task CleanupExecutionAsync()
        {
            var previous = executor;
            executor = null;
            (previous as IDisposable)?.Dispose();
            return Task.CompletedTask;
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
                try
                {
                    if (executor.Current is IYieldAction yieldBefore)
                    {
                        await yieldBefore.BeforeNextAsync();
                        isMoveNext = yieldBefore.IsMoveNext;
                        if (yieldBefore is YieldEvent yEvent)
                        {
                            if (yEvent.EventContext is null)
                            { PublishEvent(yEvent.EventIndex); }
                            else
                            { PublishEvent(yEvent.EventIndex, yEvent.EventContext); }
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
                        yieldAfter.Control = Control;
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
                Control.CheckPause();
            }
        }

        private IEnumerator? executor;

        protected override void Dispose(bool disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(disposing);
        }

        internal override Task CleanupExecutionAsync()
        {
            var previous = executor;
            executor = null;
            (previous as IDisposable)?.Dispose();
            return Task.CompletedTask;
        }
    }

}