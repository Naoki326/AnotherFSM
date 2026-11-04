using System.Diagnostics.CodeAnalysis;
using StateMachine;

namespace FSMDemo.Runtime;

/// <summary>Closed browser capability list; no assembly scanning or dynamic plugins.</summary>
public sealed class BrowserNodeFactory : IFSMNodeFactory
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);

    public BrowserNodeFactory()
    {
        Register<StartNode>("Start");
        Register<EndNode>("End");
        Register<IdleNode>("Idle");
        Register<SleepNode>("Sleep");
        Register<AccumulateNode>("Accumulate");
        Register<GroupNode>("Group");
        Register<ParallelNode>("Parallel");
    }

    // DSL properties, Branch setters and composite descriptors are accessed by reflection.
    // Preserve their complete contracts in trimmed Release builds, not just constructors.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FSMDescribe))]
    private void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(string key) where T : IFSMNode
        => _types.Add(key, typeof(T));

    public IFSMNode CreateNode(string featureName) => (IFSMNode)Activator.CreateInstance(GetNodeType(featureName))!;

    public Type GetNodeType(string featureName) => _types.TryGetValue(featureName, out var type)
        ? type : throw new InvalidOperationException($"Node type '{featureName}' is not available in the browser runtime.");

    public string GetNodeFeatureName(Type type) => _types.FirstOrDefault(pair => pair.Value == type).Key
        ?? throw new InvalidOperationException($"Node type '{type.Name}' is not available in the browser runtime.");
}
