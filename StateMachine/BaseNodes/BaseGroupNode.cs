using System.Diagnostics;

namespace StateMachine
{
    [DebuggerNonUserCode]
    public abstract class BaseGroupNode : AsyncEnumFSMNode, IObservable<StateTrackInfo>, IObservable<ExecuteTrackInfo>
    {
        public abstract IDisposable Subscribe(IObserver<StateTrackInfo> observer);
        public abstract IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer);
    }

    [DebuggerNonUserCode]
    public abstract class BaseGroupNode<T> : AsyncEnumFSMNode<T>, IObservable<StateTrackInfo>, IObservable<ExecuteTrackInfo>
        where T : class
    {
        public abstract IDisposable Subscribe(IObserver<StateTrackInfo> observer);
        public abstract IDisposable Subscribe(IObserver<ExecuteTrackInfo> observer);
    }
}
