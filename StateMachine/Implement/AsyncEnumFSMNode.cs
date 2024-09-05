using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StateMachine.Implement
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

        private bool CheckPriority(object current)
        {
            if (current is int priority && priority < Context.ManualLevel)
            {
                return true;
            }
            else if (current is Enum epriority && Convert.ToInt64(epriority) < Context.ManualLevel)
            {
                return true;
            }
            return false;
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
                    if (CheckPriority(executor.Current))
                    {
                        Pause();
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
                    }
                    catch (Exception e2)
                    {
                        throw new Exception($"Node {(this as IFSMNode).Name} 处理函数抛出异常！", e2);
                    }
                }
            }
            Context.CheckPause();
        }

        private IAsyncEnumerator<object> executor;

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

        private bool CheckPriority(object current)
        {
            if (Context is null)
            {
                return false;
            }
            if (current is int priority && priority < Context.ManualLevel)
            {
                return true;
            }
            else if (current is Enum epriority && Convert.ToInt64(epriority) < Context.ManualLevel)
            {
                return true;
            }
            return false;
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
                    if (CheckPriority(executor.Current))
                    {
                        Pause();
                    }
                }
                catch (Exception e)
                {
                    bool flag;
                    try
                    {
                        flag = HandleException(e);
                    }
                    catch (Exception e2)
                    {
                        throw new Exception($"State {(this as IFSMNode).Name} 处理函数抛出异常！", e2);
                    }
                    if (!flag)
                    {
                        throw new Exception($"State {(this as IFSMNode).Name} 存在异常未处理", e);
                    }
                }
                Context?.CheckPause();
            }
        }

        private IAsyncEnumerator<object> executor;

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

        private bool CheckPriority(object current)
        {
            if (current is int priority && priority < Context.ManualLevel)
            {
                return true;
            }
            else if (current is Enum epriority && Convert.ToInt64(epriority) < Context.ManualLevel)
            {
                return true;
            }
            return false;
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
                    if (CheckPriority(executor.Current))
                    {
                        Pause();
                    }
                }
                catch (Exception e)
                {
                    bool flag;
                    try
                    {
                        flag = HandleException(e);
                    }
                    catch (Exception e2)
                    {
                        throw new Exception($"State {(this as IFSMNode).Name} 处理函数抛出异常！", e2);
                    }
                    if (!flag)
                    {
                        throw new Exception($"State {(this as IFSMNode).Name} 存在异常未处理", e);
                    }
                }
                Context.CheckPause();
            }
        }

        private IAsyncEnumerator<object> executor;

        protected override void Dispose(bool Disposing)
        {
            if (executor != null)
                executor.DisposeAsync();
            base.Dispose(Disposing);
        }
    }
}
