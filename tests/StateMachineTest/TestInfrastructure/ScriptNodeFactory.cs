using StateMachine;

namespace StateMachineTest.TestInfrastructure;

/// <summary>
/// 为脚本测试提供真实的 BaseFSMNode 分支和转移行为，不替换引擎内部逻辑。
/// 这些测试只构建流程，不启动执行器。
/// </summary>
internal sealed class ScriptNodeFactory : IFSMNodeFactory
{
    public IFSMNode CreateNode(string featureName)
    {
        GetNodeType(featureName);
        return new StepNode();
    }

    public Type GetNodeType(string featureName) => featureName == "Step"
        ? typeof(StepNode)
        : throw new ArgumentException($"Unknown node type: {featureName}", nameof(featureName));

    public string GetNodeFeatureName(Type type) => type == typeof(StepNode)
        ? "Step"
        : throw new ArgumentException($"Unknown node type: {type}", nameof(type));

    private sealed class StepNode : BaseFSMNode
    {
        protected override Task ExecuteAsync() => Task.CompletedTask;
    }
}
