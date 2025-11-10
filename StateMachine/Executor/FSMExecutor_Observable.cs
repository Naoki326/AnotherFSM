using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace StateMachine
{

    public partial class FSMExecutor : IObservable<StateTrackInfo>, IObservable<ExecuteTrackInfo>
    {
        private readonly EventLoopScheduler eventLoopScheduler = new EventLoopScheduler();
        private readonly Subject<StateTrackInfo> stateTrackObservable = new Subject<StateTrackInfo>();
        private readonly Subject<ExecuteTrackInfo> executeTrackObservable = new Subject<ExecuteTrackInfo>();

        /// <summary>
        /// 将TrackStateEvent、TrackCallEvent事件关联到当前类的IObservable接口上
        /// </summary>
        /// <param name="isAsyncObserver">是否使用线程池来发出通知</param>
        private void InitObserver(bool isAsyncObserver)
        {
            var stateObserverWrapper = isAsyncObserver ? stateTrackObservable.ObserveOn(ThreadPoolScheduler.Instance) : stateTrackObservable;
            var executeObserverWrapper = isAsyncObserver ? executeTrackObservable.ObserveOn(ThreadPoolScheduler.Instance) : executeTrackObservable;

            stateObserverWrapper.Subscribe((info) =>
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

            executeObserverWrapper.Subscribe((info) =>
            {
                FSMStateChanged?.Invoke(this, info.CurrentState, info.LastState);
            }, (ex) =>
            {

            });
        }

        IDisposable IObservable<StateTrackInfo>.Subscribe(IObserver<StateTrackInfo> observer)
        {
            return stateTrackObservable.ObserveOn(eventLoopScheduler).Subscribe(observer);
        }

        IDisposable IObservable<ExecuteTrackInfo>.Subscribe(IObserver<ExecuteTrackInfo> observer)
        {
            return executeTrackObservable.ObserveOn(eventLoopScheduler).Subscribe(observer);
        }

        public event EventHandler<string>? NodeStateChanged;
        public event EventHandler<string>? NodeExitChanged;
        //事件的参数：solver实例，新状态，前一状态
        public event Action<FSMExecutor, FSMState, FSMState>? FSMStateChanged;
        private void TrackFSMStateChanged(FSMState current, FSMState previousState)
        {
            executeTrackObservable.OnNext(new ExecuteTrackInfo()
            {
                LastState = previousState,
                CurrentState = current
            });
        }

        private void TrackCallname([CallerMemberName] string info = default!)
        {
            stateTrackObservable.OnNext(new StateTrackInfo() { IsCallEvent = true, CallMethodName = info });
        }



        private void TrackStart(long threadId)
        {
            stateTrackObservable.OnNext(new StateTrackInfo()
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
            stateTrackObservable.OnNext(new StateTrackInfo()
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
            stateTrackObservable.OnNext(new StateTrackInfo()
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

            stateTrackObservable.OnNext(new StateTrackInfo()
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
            stateTrackObservable.OnNext(new StateTrackInfo()
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
            stateTrackObservable.OnNext(new StateTrackInfo()
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
            stateTrackObservable.OnNext(new StateTrackInfo()
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
            stateTrackObservable.OnNext(new StateTrackInfo()
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
