using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace StateMachine
{

    public partial class FSMExecutor : IObservable<StateTrackInfo>
    {
        private readonly Subject<StateTrackInfo> observable = new Subject<StateTrackInfo>();


        //将TrackStateEvent、TrackCallEvent事件关联到当前类的IObservable接口上
        private void InitObserver()
        {
            observable.ObserveOn(ThreadPoolScheduler.Instance).Subscribe((info) =>
            {
                if (!info.IsCallEvent)
                {
                    if(info.TrackType == TrackType.Start)
                    {
                        NodeStateChanged?.Invoke(this, info.StateName);
                    }
                    else if(info.TrackType == TrackType.Normal)
                    {
                        if(info.IsEnter)
                        {
                            NodeStateChanged?.Invoke(this, info.StateName);
                        }
                        else
                        {
                            NodeExitChanged?.Invoke(this, info.StateName);
                        }
                    }
                }
            }, (ex) =>
            {

            });
        }

        ////流程节点进出事件
        //public event Action<StateTrackInfo>? TrackStateEvent;

        ////Restart\Pause\Continue\Stop等方法调用事件
        //public event Action<string>? TrackCallEvent;

        //public event Action<Exception>? NodeExceptionEvent;

        IDisposable IObservable<StateTrackInfo>.Subscribe(IObserver<StateTrackInfo> observer)
        {
            return observable.ObserveOn(ThreadPoolScheduler.Instance).Subscribe(observer);
        }

        public void StopTrack()
        {
            observable.OnCompleted();
        }

        public event EventHandler<string>? NodeStateChanged;
        private void NodeStateChangedInvoke(string nodeName)
        {
            NodeStateChanged?.Invoke(this, nodeName);
        }
        public event EventHandler<string>? NodeExitChanged;
        private void NodeExitChangedInvoke(string nodeName)
        {
            NodeExitChanged?.Invoke(this, nodeName);
        }
        //事件的参数：solver实例，新状态，前一状态
        public event Action<FSMExecutor, FSMState, FSMState>? FSMStateChanged;
        private void FSMStateChangedInvoke(FSMState current, FSMState previousState)
        {
            FSMStateChanged?.Invoke(this, current, previousState);
        }

        private void TrackCallname([CallerMemberName] string info = default!)
        {
            observable.OnNext(new StateTrackInfo() { IsCallEvent = true, CallMethodName = info });
        }



        private void TrackStart(long threadId)
        {
            observable.OnNext(new StateTrackInfo()
            {
                IsEnter = true,
                TrackType = TrackType.Start,
                PrevStateName = "",
                CurrentNode = start,
                StateName = start.Name,
                FSMEvent = default!,
                EventName = "",
                ThreadId = threadId,
            });
        }

        private void TrackStartEnd(bool isExit, long threadId)
        {
            observable.OnNext(new StateTrackInfo()
            {
                IsEnter = false,
                TrackType = isExit ? TrackType.Normal : TrackType.Cancel,
                PrevStateName = "",
                CurrentNode = start,
                StateName = start.Name,
                FSMEvent = default!,
                EventName = "",
                ThreadId = threadId,
            });
        }

        private void TrackContinue(long threadId)
        {
            observable.OnNext(new StateTrackInfo()
            {
                IsEnter = true,
                TrackType = TrackType.Continue,
                PrevStateName = currentNode.Name,
                CurrentNode = currentNode,
                StateName = currentNode.Name,
                EventName = ContinueEvent.EventName,
                FSMEvent = ContinueEvent,
                ThreadId = threadId,
            });
        }

        private void TrackContinueEnd(bool isExit, long threadId)
        {

            observable.OnNext(new StateTrackInfo()
            {
                IsEnter = false,
                TrackType = isExit ? TrackType.Normal : TrackType.Cancel,
                PrevStateName = "",
                CurrentNode = currentNode,
                StateName = currentNode.Name,
                FSMEvent = default!,
                EventName = "",
                ThreadId = threadId,
            });
        }


        private void TrackNoUseEvent(long threadId, FSMEvent @event)
        {
            observable.OnNext(new StateTrackInfo()
            {
                TrackType = TrackType.DiscardEvent,
                PrevStateName = "",
                CurrentNode = currentNode,
                StateName = currentNode.Name,
                FSMEvent = @event,
                EventName = @event.EventName,
                ThreadId = threadId,
            });
        }

        private void TrackStateExit(long threadId, bool isExit)
        {
            observable.OnNext(new StateTrackInfo()
            {
                IsEnter = false,
                TrackType = isExit ? TrackType.Normal : TrackType.Cancel,
                PrevStateName = currentNode.Name,
                StateName = currentNode.Name,
                CurrentNode = currentNode,
                FSMEvent = default!,
                EventName = "",
                ThreadId = threadId,
            });
        }

        private void TrackStateEnter(long threadId, FSMEvent @event, IFSMNode nextNode)
        {
            observable.OnNext(new StateTrackInfo()
            {
                IsEnter = true,
                TrackType = TrackType.Normal,
                PrevStateName = currentNode.Name,
                StateName = nextNode.Name,
                CurrentNode = nextNode,
                FSMEvent = @event,
                EventName = @event.EventName,
                ThreadId = threadId,
            });
        }

        private void TrackFSMExit(long threadId)
        {
            observable.OnNext(new StateTrackInfo()
            {
                IsEnter = false,
                TrackType = TrackType.StateError,
                PrevStateName = currentNode.Name,
                StateName = currentNode.Name,
                CurrentNode = currentNode,
                FSMEvent = default!,
                EventName = "",
                ThreadId = threadId,
            });
        }

    }

}
