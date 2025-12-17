using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace StateMachine
{

    [DebuggerNonUserCode]
    internal class YieldTryCatch : IYieldAction
    {
        public YieldEnum Result { get; set; } = YieldEnum.None;

        public bool IsMoveNext { get; set; } = true;

        public FSMNodeContext Context { set { } }

        public IExecuterContext SolveContext { set { } }

        Action<Exception> _doWhenException;
        Func<Task> _doTask;
        Action _doFinally;
        Func<Task> _doFinallyAsync;
        public YieldTryCatch(Func<Task> doTask, Action<Exception> doWhenException)
        {
            _doTask = doTask;
            _doWhenException = doWhenException;
        }

        public YieldTryCatch(Func<Task> doTask, Action<Exception> doWhenException, Action doFinally)
        {
            _doTask = doTask;
            _doWhenException = doWhenException;
            _doFinally = doFinally;
        }

        public YieldTryCatch(Func<Task> doTask, Action<Exception> doWhenException, Func<Task> doFinally)
        {
            _doTask = doTask;
            _doWhenException = doWhenException;
            _doFinallyAsync = doFinally;
        }

        public Task AfterYieldAsync()
        {
            return Task.CompletedTask;
        }

        public async Task BeforeNextAsync()
        {
            try
            {
                await _doTask();
                IsMoveNext = true;
            }
            catch (OperationCanceledException)
            {
                IsMoveNext = false;
            }
            catch (Exception ex) when (ex.InnerException is OperationCanceledException)
            {
                IsMoveNext = false;
            }
            catch (Exception e)
            {
                _doWhenException?.Invoke(e);
            }
            finally
            {
                if (_doFinally is not null)
                    _doFinally.Invoke();
                if (_doFinallyAsync is not null)
                    await _doFinallyAsync();
            }
        }
    }
}
