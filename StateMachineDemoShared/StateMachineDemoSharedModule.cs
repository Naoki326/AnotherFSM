using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using StateMachine;
using Module = Autofac.Module;

namespace StateMachineDemoShared
{
    internal class StateMachineDemoSharedModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
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
