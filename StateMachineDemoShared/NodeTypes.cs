using Autofac;
using Autofac.Core;
using StateMachine;

namespace DemoNodes
{
    public class NodeTypes
    {

        private ILifetimeScope container;

        public NodeTypes(ILifetimeScope container)
        {
            this.container = container;
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
