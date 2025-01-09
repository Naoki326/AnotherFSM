using System.Diagnostics;

namespace StateMachine
{

    //流程上下文
    public partial class FSMNodeContext
    {
        public long ManualLevel { get; set; } = 0;

        public FSMNodeContext()
        {
            TokenSource = new CancellationTokenSource();
            IsPaused = false;
        }

        public FSMNodeContext(FSMNodeContext p)
        {
            this.TokenSource = p.TokenSource;
            this.TriggerEvent = p.TriggerEvent;
            this.IsPaused = p.IsPaused;
            this.EnumResult = p.EnumResult;
            this.Data = p.Data;
        }

        [DebuggerStepThrough]
        internal void SetTokenSource(CancellationTokenSource tokenSource)
        {
            TokenSource = tokenSource;
            IsPaused = false;
        }

        [DebuggerStepThrough]
        internal void SetPause(bool pause)
        {
            IsPaused = pause;
        }

        [DebuggerNonUserCode]
        public CancellationToken Token => TokenSource.Token;

        [DebuggerNonUserCode]
        //在写流程的时候要通过这些东西来实现暂停的功能
        internal CancellationTokenSource TokenSource { get; private set; }

        [DebuggerStepThrough]
        public void Pause()
        {
            TokenSource?.Cancel();
            IsPaused = true;
        }

        [DebuggerStepThrough]
        public void CheckPause()
        {
            TokenSource.Token.ThrowIfCancellationRequested();
        }
        [DebuggerStepThrough]
        public bool IsPauseRequested()
        {
            return TokenSource.Token.IsCancellationRequested;
        }
        public bool IsPaused { get; private set; } = false;




        [DebuggerNonUserCode]
        public FSMEvent TriggerEvent { get; set; } = default!;




        public object Data { get; set; } = default!;




        public int EnumResult { get; set; }


    }

    public class FSMNodeContext<T> : FSMNodeContext where T : class
    {
        public FSMNodeContext() { }

        public FSMNodeContext(FSMNodeContext p) : base(p) { }

        public FSMNodeContext<U> To<U>() where U : class
        {
            return new FSMNodeContext<U>(this);
        }
        public new T Data
        {
            get
            {
                if (base.Data is T data)
                    return data;
                else
                    return null;
            }
            set
            {
                base.Data = value;
            }
        }
    }

}
