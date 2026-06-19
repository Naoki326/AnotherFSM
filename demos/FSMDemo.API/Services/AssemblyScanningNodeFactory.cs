using System.Reflection;
using StateMachine;

namespace FSMDemo.API.Services;

public class AssemblyScanningNodeFactory : IFSMNodeFactory
{
    private readonly Dictionary<string, Type> _nodeTypes = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyScanningNodeFactory()
    {
        ScanDirectory();
    }

    private void ScanDirectory()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                ScanAssembly(assembly);
            }
            catch
            {
            }
        }
        Console.WriteLine($"[AssemblyScanningNodeFactory] Found {_nodeTypes.Count} node types: {string.Join(", ", _nodeTypes.Keys)}");
    }

    private void ScanAssembly(Assembly assembly)
    {
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(IFSMNode).IsAssignableFrom(type))
                    continue;

                var attr = type.GetCustomAttribute<FSMNodeAttribute>();
                if (attr == null || string.IsNullOrEmpty(attr.Key))
                    continue;

                if (!_nodeTypes.ContainsKey(attr.Key))
                    _nodeTypes[attr.Key] = type;
            }
        }
        catch
        {
        }
    }

    public IFSMNode CreateNode(string featureName)
    {
        if (_nodeTypes.TryGetValue(featureName, out var type))
            return (IFSMNode)Activator.CreateInstance(type)!;

        var availableTypes = string.Join(", ", _nodeTypes.Keys);
        throw new InvalidOperationException(
            $"Node type '{featureName}' not registered. Available types: {availableTypes}");
    }

    public Type GetNodeType(string featureName)
    {
        if (_nodeTypes.TryGetValue(featureName, out var type))
            return type;

        throw new InvalidOperationException($"Node type '{featureName}' not registered.");
    }

    public string GetNodeFeatureName(Type type)
    {
        var attr = type.GetCustomAttribute<FSMNodeAttribute>();
        return attr?.Key ?? type.Name;
    }
}
