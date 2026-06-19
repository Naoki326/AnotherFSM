using System.Reflection;
using StateMachine;

namespace FSMDemo.Wasm.Services;

public sealed class BrowserNodeFactory : IFSMNodeFactory
{
    private readonly Dictionary<string, Type> nodeTypes = new(StringComparer.OrdinalIgnoreCase);

    public BrowserNodeFactory()
    {
        RegisterFromAssembly(typeof(FSMEngine).Assembly);
        RegisterFromAssembly(typeof(StartNode).Assembly);
    }

    private void RegisterFromAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.ContainsGenericParameters || !typeof(IFSMNode).IsAssignableFrom(type))
            {
                continue;
            }

            var attr = type.GetCustomAttribute<FSMNodeAttribute>();
            if (attr?.Key is { Length: > 0 } key)
            {
                nodeTypes.TryAdd(key, type);
            }
        }
    }

    public IFSMNode CreateNode(string featureName)
    {
        if (!nodeTypes.TryGetValue(featureName, out var type))
        {
            throw new InvalidOperationException(
                $"Node type '{featureName}' is not available in the browser demo. Available nodes: {string.Join(", ", nodeTypes.Keys)}");
        }

        return (IFSMNode)Activator.CreateInstance(type)!;
    }

    public Type GetNodeType(string featureName)
    {
        if (nodeTypes.TryGetValue(featureName, out var type))
        {
            return type;
        }

        throw new InvalidOperationException($"Node type '{featureName}' is not available in the browser demo.");
    }

    public string GetNodeFeatureName(Type type)
    {
        return type.GetCustomAttribute<FSMNodeAttribute>()?.Key ?? type.Name;
    }
}
