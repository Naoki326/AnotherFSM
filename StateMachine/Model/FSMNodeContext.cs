using System.Diagnostics;
using System.Reactive.Linq;

namespace StateMachine
{

    // Transitional legacy control/Data wrapper, retained until consumer migration (#21/#22).
    // Not the business Context of the typed binding path.
    [DebuggerNonUserCode]
    public partial class FSMNodeContext : FSMExecutionControl
    {
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

        ~FSMNodeContext()
        {
        }

        public FSMEvent TriggerEvent { get; set; } = default!;

        public object Data { get; set; } = default!;
        public int EnumResult { get; set; }


        public FSMNodeContext<U> As<U>() where U : class
        {
            return new FSMNodeContext<U>(this);
        }
    }

    [DebuggerNonUserCode]
    public class FSMNodeContext<T> : FSMNodeContext where T : class
    {
        public FSMNodeContext() { }

        public FSMNodeContext(FSMNodeContext p) : base(p) { }


        public new T Data
        {
            get
            {
                if (base.Data is T data)
                    return data;
                else
                    return default!;
            }
            set
            {
                base.Data = value;
            }
        }
    }

}
