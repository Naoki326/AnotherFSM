namespace StateMachine
{
    /// <summary>A concrete execution round. Never follows an executor's subsequent restart.</summary>
    public sealed class FSMExecution : IFSMEventSharingExecution
    {
        private readonly FSMExecutor executor;
        private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal FSMEventSharing.Member Member { get; }
        internal FSMExecution(FSMExecutor executor, FSMEventSharing.Member member) { this.executor = executor; Member = member; }
        public Task Completion => completion.Task;
        public FSMState State => Completion.IsCompleted ? finalState : executor.State;
        private FSMState finalState;
        internal void Complete(FSMState state, Exception? error)
        {
            finalState = state;
            if (error == null) completion.TrySetResult(true);
            else completion.TrySetException(error);
        }
        public void PublishEvent(string name) => PublishEvent(name, null);
        public void PublishEvent<T>(string name, T payload) => PublishEvent(name, (object?)payload);
        private void PublishEvent(string name, object? payload)
            => executor.PublishToExecution(this, name, payload);
        public void Pause() => executor.ForExecution(this, () => { executor.Pause(); return true; });
        public Task<bool> PauseAsync() => executor.ForExecution(this, executor.PauseAsync);
        public bool Continue() => executor.ForExecution(this, executor.Continue);
        public void Stop() => executor.Stop(this);
        public Task<bool> StopAsync() => executor.StopAsync(this);
    }
}
