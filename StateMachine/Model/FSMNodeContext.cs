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

        internal void SetTokenSource(CancellationTokenSource tokenSource)
        {
            TokenSource = tokenSource;
            IsPaused = false;
        }

        internal void SetPause(bool pause)
        {
            IsPaused = pause;
        }

        public CancellationToken Token => TokenSource.Token;

        //在写流程的时候要通过这些东西来实现暂停的功能
        internal CancellationTokenSource TokenSource { get; private set; }

        internal void Pause()
        {
            TokenSource?.Cancel();
            IsPaused = true;
        }

        public void CheckPause()
        {
            TokenSource.Token.ThrowIfCancellationRequested();
        }
        public bool IsPauseRequested()
        {
            return TokenSource.Token.IsCancellationRequested;
        }
        public bool IsPaused { get; private set; } = false;




        public FSMEvent TriggerEvent { get; set; }




        public object Data { get; set; }




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
