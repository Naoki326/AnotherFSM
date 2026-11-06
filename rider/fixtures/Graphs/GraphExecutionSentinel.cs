using System.Runtime.CompilerServices;
using StateMachine;
namespace Graphs;

/// <summary>
/// Controlled fixture only. Not linked into StateMachine or any runtime regression project.
/// These paths are deliberately executable business code, not static editor configuration.
/// </summary>
internal static class GraphExecutionSentinel
{
    internal const string MarkerName = "graph-business-execution.marker";

    internal static T Forbidden<T>(string entryPoint, [CallerFilePath] string sourcePath = "")
    {
        // The sandbox passes its evidence directory to the backend process. The fallback records
        // accidental execution during explicitly authorized fixture preparation, separately from
        // the host run. Never derive this destination from the IDE's working directory.
        var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "../../.."));
        var evidence = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR")
            ?? Path.Combine(repository, ".scratch", "issue-33", "host");
        try
        {
            Directory.CreateDirectory(evidence);
            File.AppendAllText(Path.Combine(evidence, MarkerName),
                $"{DateTime.UtcNow:O}\tpid={Environment.ProcessId}\t{entryPoint}\tsource={sourcePath}{Environment.NewLine}");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"BUSINESS_EXECUTION_SENTINEL: {entryPoint}; marker write failed at {evidence}", error);
        }
        throw new InvalidOperationException($"BUSINESS_EXECUTION_SENTINEL: {entryPoint}");
    }
}

// A custom factory is NOT an editor registration convention. Looking up GraphKey must use static
// FSMNode metadata, never instantiate/call this factory or evaluate its registration getter.
public sealed class GraphFactory : IFSMNodeFactory
{
    public GraphFactory() => GraphExecutionSentinel.Forbidden<object>("GraphFactory.ctor");
    public static IFSMNodeFactory Current => GraphExecutionSentinel.Forbidden<IFSMNodeFactory>("GraphFactory.Current.get");
    public IFSMNode CreateNode(string featureName) => GraphExecutionSentinel.Forbidden<IFSMNode>("GraphFactory.CreateNode");
    public Type GetNodeType(string featureName) => GraphExecutionSentinel.Forbidden<Type>("GraphFactory.GetNodeType");
    public string GetNodeFeatureName(Type type) => GraphExecutionSentinel.Forbidden<string>("GraphFactory.GetNodeFeatureName");
}
