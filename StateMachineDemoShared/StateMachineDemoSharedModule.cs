using System.Reflection;
using Autofac;
using DemoNodes;
using StateMachine;
using Module = Autofac.Module;

namespace StateMachineDemoShared
{
    internal class StateMachineDemoSharedModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<NodeTypes>().SingleInstance();
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .AssignableTo<IFSMNode>()
                .As(t =>
                {
                    string? key = (t.GetCustomAttribute(typeof(FSMNodeAttribute)) as FSMNodeAttribute)?.Key;
                    if (key is not null)
                        return new Autofac.Core.KeyedService(key, typeof(IFSMNode));
                    throw new InvalidOperationException("DeviceImplInject key has not set!");
                })
                .InstancePerDependency();
            base.Load(builder);
        }
    }
}
