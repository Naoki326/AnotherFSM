using System.Reflection;

namespace StateMachine
{

    /// <summary>
    /// 该接口将作为StateMachine的FSMEngine类型的节点构造工厂
    /// </summary>
    public interface IFSMNodeFactory
    {
        // 创建节点
        IFSMNode CreateNode(string featureName);

        // 获取该key对应的节点类型
        Type GetNodeType(string featureName);

        string GetNodeFeatureName(Type type);
    }

}
