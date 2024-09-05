namespace StateMachine
{
    //状态连接线
    public sealed class FSMTransition
    {
        //源状态
        public IFSMNode Source { get; }

        //目标状态
        public IFSMNode Target { get; }

        //触发事件
        public FSMEvent Trigger { get; }

        public FSMTransition(IFSMNode source, FSMEvent trigger, IFSMNode target)
        {
            this.Source = source;
            this.Target = target;
            this.Trigger = trigger;
        }

        public override string ToString()
        {
            return $"{Trigger.EventName}->{Source.Name} to {Target.Name};\r\n";
        }

    }
}
