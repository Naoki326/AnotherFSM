using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Autofac;
using StateMachine;
using Module = Autofac.Module;

namespace BaseNodes
{
    internal class BaseNodesModule : Module
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
            RegisterKeyedNode<GroupNode>(builder);
            RegisterKeyedNode<ParallelNode>(builder);
            builder.RegisterType<AutofacNodeFactory>().As<IFSMNodeFactory>().SingleInstance();
            base.Load(builder);
        }

        private void RegisterKeyedNode<T>(ContainerBuilder builder) where T : IFSMNode
        {
            if (typeof(T).GetCustomAttribute(typeof(FSMNodeAttribute)) is FSMNodeAttribute attr)
            {
                builder.RegisterType<T>().Keyed<IFSMNode>(attr.Key);
            }
        }
    }
}
