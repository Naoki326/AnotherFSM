using System.Diagnostics;
using System.Reactive.Subjects;

namespace StateMachine
{

    public partial class FSMExecutor : IObservable<StateTrackInfo>
    {
        private readonly Subject<StateTrackInfo> observable = new Subject<StateTrackInfo>();

        //将TrackStateEvent、TrackCallEvent事件关联到当前类的IObservable接口上
        private void InitObserver()
        {
            //this.Subscribe((info) =>
            //{
            //    if (!info.IsCallEvent)
            //    {
            //        TrackStateEvent?.Invoke(info);
            //    }
            //    else
            //    {
            //        TrackCallEvent?.Invoke(info.CallMethodName);
            //    }
            //}, (ex)=> 
            //{
            //    NodeExceptionEvent?.Invoke(ex);
            //});
        }

        ////流程节点进出事件
        //public event Action<StateTrackInfo>? TrackStateEvent;

        ////Restart\Pause\Continue\Stop等方法调用事件
        //public event Action<string>? TrackCallEvent;

        //public event Action<Exception>? NodeExceptionEvent;

        IDisposable IObservable<StateTrackInfo>.Subscribe(IObserver<StateTrackInfo> observer)
        {
            return observable.Subscribe(observer);
        }

        public void StopTrack()
        {
            observable.OnCompleted();
        }

    }

}
