namespace StateMachine
{
    /// <summary>Execution control only. Contains neither business objects nor event payloads.</summary>
    public class FSMExecutionControl
    {
        public CancellationToken Token => TokenSource.Token;
        internal CancellationTokenSource TokenSource { get; private protected set; } = new CancellationTokenSource();
        public bool IsPaused { get; private protected set; }

        internal void SetTokenSource(CancellationTokenSource source)
        {
            TokenSource = source;
            IsPaused = false;
        }
        internal void SetPause(bool paused) => IsPaused = paused;
        public void Pause()
        {
            TokenSource.Cancel();
            IsPaused = true;
        }
        public void CheckPause() => Token.ThrowIfCancellationRequested();
        public bool IsPauseRequested() => Token.IsCancellationRequested;
    }
}
