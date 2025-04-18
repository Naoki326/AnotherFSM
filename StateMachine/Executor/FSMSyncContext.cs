using System.Collections.Concurrent;
using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class FSMSyncContext : SynchronizationContext, IDisposable
    {

        private Lazy<BlockingCollection<(SendOrPostCallback d, object state)>> workItems => new(() =>
        {
            var items = new BlockingCollection<(SendOrPostCallback d, object state)>();
            Task.Factory.StartNew(() =>
            {
                SetSynchronizationContext(this);
                try
                {
                    foreach (var (d, state) in items.GetConsumingEnumerable(cts.Token))
                    {
                        d(state);
                    }
                } catch (Exception) { }
            }, TaskCreationOptions.LongRunning);
            return items;
        });

        private CancellationTokenSource cts = new CancellationTokenSource();
        private bool disposedValue;

        public FSMSyncContext()
        {
        }

        /// <inheritdoc />
        public override void Post(SendOrPostCallback d, object state)
        {
            if (!workItems.Value.IsCompleted)
            {
                workItems.Value.TryAdd((d, state));
            }
            else
            {
                d(state);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                }

                // TODO: 释放未托管的资源(未托管的对象)并重写终结器
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
                if (!workItems.Value.IsCompleted)
                {
                    workItems.Value.CompleteAdding();
                }
                // TODO: 将大型字段设置为 null
                disposedValue = true;
            }
        }

        // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        ~FSMSyncContext()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
