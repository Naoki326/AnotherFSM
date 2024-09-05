using Autofac;
using Module = Autofac.Module;

namespace StateMachine
{
    internal class StateMachineModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.Register(c => new EventAggregator()).Keyed<IEventAggregator>("FSM").SingleInstance();
            base.Load(builder);
        }

    }
}
