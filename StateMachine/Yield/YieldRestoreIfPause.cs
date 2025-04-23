using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    internal class YieldRestoreIfPause : IYieldAction, IDisposable
    {
        private readonly Func<Task> restore;

        public YieldEnum Result => YieldEnum.None;

        public bool IsMoveNext { get; set; } = true;

        bool isRestore = false;

        public FSMNodeContext Context { set; get; }

        public YieldRestoreIfPause(Func<Task> restore)
        {
            this.restore = restore;
        }

        public Task AfterYieldAsync()
        {
            restoreRegistration = Context.Token.Register(ChangeRestore);
            return Task.CompletedTask;
        }

        private IDisposable restoreRegistration;
        private bool disposedValue;

        private void ChangeRestore()
        {
            isRestore = true;
            IsMoveNext = false;
        }

        public async Task BeforeNextAsync()
        {
            restoreRegistration?.Dispose();
            if (isRestore)
                await restore();
            IsMoveNext = true;
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
                restoreRegistration?.Dispose();
                // TODO: 将大型字段设置为 null
                disposedValue = true;
            }
        }

        // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        ~YieldRestoreIfPause()
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
