using System.Reflection;
using Autofac;
using StateMachine;
using Module = Autofac.Module;

namespace DemoNodes
{

    /// <summary>
    /// 演示，使用Autofac注入设计的Demo节点
    /// 注入时按照FSMNodeAttribute特性标记的Key作为容器的Key
    /// 需要额外注意在注入时将StateMachine中的GroupNode和ParalleNode也注入到容器中
    /// </summary>
    internal class DemoNodesModule : Module
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
