using System.Runtime.CompilerServices;

namespace StateMachine
{
    public enum TrackType
    {
        Normal,
        Start,
        Cancel,
        Continue,
        StateError,
        DiscardEvent,
    }

    public class StateTrackInfo
    {
        public bool IsEnter { get; set; }
        public TrackType TrackType { get; set; }
        public string StateName { get; set; } = "";
        public string PrevStateName { get; set; } = "";
        public string EventName { get; set; } = "";
        public IFSMNode CurrentNode { get; set; } = default!;
        public FSMEvent FSMEvent { get; set; } = default!;
        public long ThreadId { get; set; }

        //新增追踪调用函数的事件
        public bool IsCallEvent { get; set; } = false;
        public string CallMethodName { get; set; } = "";

    }

    public interface IFSMNodeTracker : IObserver<StateTrackInfo>, IObserver<string>
    {
        void EnterState(TrackType trackType, string stateName, string prevStateName, string eventName,
            IFSMNode currentNode, FSMEvent FSMEvent, long ThreadId);

        void ExitState(TrackType trackType, string stateName, IFSMNode currentNode, long ThreadId);

        void RecordCallingMethod([CallerMemberName] string methodName = default!);
    }
}
