using Autofac;

namespace StateMachine.FlowComponent
{
    internal class StateMachineFlowModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<StateMachineFlowJSModule>().InstancePerLifetimeScope();
            base.Load(builder);
        }
    }
}
