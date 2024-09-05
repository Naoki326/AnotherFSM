using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

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

        protected override Task ExecuteMethodAsync()
        {
            if (executor is null)
            {
                executor = ExecuteEnumerable().GetEnumerator();
            }
            if (executor is null)
            {
                return Task.CompletedTask;
            }
            while (true)
            {
                Context.CheckPause();
                try
                {
                    if (!executor.MoveNext())
                    {
                        (executor as IEnumerator<object>)?.Dispose();
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
            }
            Context.CheckPause();
            return Task.CompletedTask;
        }

        private IEnumerator executor;

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

        protected override Task ExecuteMethodAsync()
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

            return Task.CompletedTask;
        }

        private IEnumerator executor;

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

        protected override Task ExecuteMethodAsync()
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

            return Task.CompletedTask;
        }

        private IEnumerator executor;

        protected override void Dispose(bool Disposing)
        {
            (executor as IEnumerator<object>)?.Dispose();
            base.Dispose(Disposing);
        }
    }
}
