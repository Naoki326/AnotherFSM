using Autofac;
using Autofac.Core;
using StateMachine;

namespace StateMachineDemoShared
{
    public class ContainerWrapper : IContainerWrapper
    {
        protected ILifetimeScope Container { get; }

        public ContainerWrapper(ILifetimeScope container)
        {
            Container = container;
        }

        public T GetInstance<T>(string key) where T : notnull
        {
            if (string.IsNullOrEmpty(key))
            {
                return Container.Resolve<T>();
            }
            return Container.ResolveKeyed<T>(key);
        }

        public IEnumerable<T> GetAllInstances<T>(string key) where T : notnull
        {
            if (string.IsNullOrEmpty(key))
            {
                return Container.Resolve<IEnumerable<T>>();
            }
            return Container.ResolveKeyed<IEnumerable<T>>(key);
        }

        public IEnumerable<object> GetAllKeys<T>() where T : notnull
        {
            return Container.ComponentRegistry.Registrations
                .Where(r => r.Services.OfType<KeyedService>().Any(p => p.ServiceType == typeof(T)))
                .Select(r => r.Services.OfType<KeyedService>().Select(ks => ks.ServiceKey))
                .SelectMany(x => x)
                .Distinct();
        }

        public IEnumerable<Type> GetAllTypes<T>() where T : notnull
        {
            // 获取所有组件注册信息
            var componentRegistry = Container.ComponentRegistry.Registrations;

            // 过滤出所有实现了IA接口的类型
            var iaTypes = componentRegistry
                .SelectMany(r => r.Services.OfType<KeyedService>().Where(s => s.ServiceType == typeof(T))
                .Select(s => r.Activator.LimitType))
                .Distinct();

            return iaTypes;
        }

        public IEnumerable<KeyValuePair<object, Type>> GetAllKeyTypePairs<T>() where T : notnull
        {
            return Container.ComponentRegistry.Registrations
                .Where(r => r.Services.OfType<KeyedService>().Any(p => p.ServiceType == typeof(T)))
                .Select(r => r.Services.OfType<KeyedService>().Select(ks => new KeyValuePair<object, Type>(ks.ServiceKey, ks.ServiceType)))
                .SelectMany(x => x)
                .Distinct();
            throw new NotImplementedException();
        }
    }
}