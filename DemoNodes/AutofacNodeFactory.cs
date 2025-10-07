using Autofac;
using Autofac.Core;
using StateMachine;

namespace DemoNodes
{
    /// <summary>
    /// 当使用Autofac作为容器时，实现该接口
    /// 该接口将作为StateMachine的FSMEngine类型的节点构造工厂
    /// </summary>
    public class AutofacNodeFactory : IFSMNodeFactory
    {
        private ILifetimeScope container;

        public AutofacNodeFactory(ILifetimeScope container)
        {
            this.container = container;
        }

        public IFSMNode CreateNode(string name)
        {
            return container.ResolveKeyed<IFSMNode>(name);
        }

        //返回autofac的IContainer中找出keyedservice的key等于name，类型为IFSMNode的实例的Type
        public Type GetNodeType(string name)
        {
            // 查找以 Keyed 的形式注册，键匹配 `name`，服务类型是 IFSMNode
            var registration = container.ComponentRegistry.Registrations
                .FirstOrDefault(r =>
                    r.Services.OfType<KeyedService>().Any(s =>
                        s.ServiceKey.Equals(name) && s.ServiceType == typeof(IFSMNode)));

            // 如果找到对应的注册，则获取其实现类型，并返回
            if (registration != null)
            {
                return registration.Activator.LimitType;
            }

            // 如果没有找到匹配的服务，可以抛出异常或返回null
            throw new InvalidOperationException($"No IFSMNode service with key '{name}' found.");
        }

        public string GetNodeName(Type t)
        {
            var registration = container.ComponentRegistry.Registrations.FirstOrDefault(r =>
                r.Services.OfType<KeyedService>().Any(s => s.ServiceType == t));
            return (string)registration.Services.OfType<KeyedService>().First().ServiceKey;
        }

        public IEnumerable<Type> GetNodeTypes()
        {
            return container.ComponentRegistry.Registrations
                .SelectMany(r =>
                    r.Services.OfType<KeyedService>().Where(s =>
                        s.ServiceType == typeof(IFSMNode))
                .Select(s => r.Activator.LimitType))
                .Distinct();
        }
    }
}
