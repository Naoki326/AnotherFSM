namespace StateMachine
{
    //事件类
    public class FSMEvent : ICloneable
    {
        public FSMEvent(string eventName)
        {
            EventName = eventName;
        }

        public string EventID { get; } = Guid.NewGuid().ToString();

        protected object? eventContext;
        public object? EventContext
        {
            get => eventContext;
            set => eventContext = value;
        }

        public string EventName { get; private set; }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public override string ToString()
        {
            return EventName;
        }
    }
    //事件类
    public class FSMEvent<T> : FSMEvent
    {

        public FSMEvent(string name) : base(name)
        {
        }

        public new T? EventContext
        {
            get => eventContext is T t ? t : default;
            set
            {
                if (value is not null) eventContext = value;
            }
        }
    }
}
