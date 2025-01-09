namespace StateMachine
{
    internal static class FSMEventAggregator
    {

        //private static Lazy<IEventAggregator> _eventAggregator = new Lazy<IEventAggregator>(() => IoC.Get<IEventAggregator>("FSM"));
        private static Lazy<IEventAggregator> _eventAggregator = new Lazy<IEventAggregator>(() => new EventAggregator());
        public static IEventAggregator EventAggregator => _eventAggregator.Value;
    }
}
