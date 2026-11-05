namespace StateMachine;

/// <summary>Diagnostics for deliberately unsupported runtime composition inputs.</summary>
public static class RemovedComposition
{
    public const string Diagnostic = "GroupNode/ParallelNode runtime composition is no longer supported. Use an ordinary Module for definition reuse only (非等价 / not equivalent: no child lifecycle, wrapper exits or parallel join).";

    public static void RejectType(string type)
    {
        if (type.ToUpperInvariant() is "GROUP" or "GROUPT" or "GROUPTU"
            or "PARALLEL" or "PARALLELT" or "PARALLELTU" or "GROUPNODE" or "PARALLELNODE")
            throw new ScriptException($"{Diagnostic} Unsupported type: {type}.");
    }
}
