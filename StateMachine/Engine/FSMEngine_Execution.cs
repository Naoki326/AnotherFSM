using System.Runtime.CompilerServices;

namespace StateMachine
{
    public partial class FSMEngine
    {
        private sealed class NodeOwner
        {
            internal NodeOwner(FSMEngine engine, string name) { Engine = engine; Name = name; }
            internal FSMEngine Engine { get; }
            internal string Name { get; }
        }
        private static readonly ConditionalWeakTable<IFSMNode, NodeOwner> nodeOwners = new();
        private static readonly object ownershipSync = new object();
        private readonly string executionIdentity = Guid.NewGuid().ToString();

        internal IFSMNode CreateOwnedNode(string type, string name)
        {
            var node = nodeFactory.CreateNode(type);
            if (node != null) ClaimNode(node, name);
            return node!;
        }

        internal void ClaimNode(IFSMNode node, string name)
        {
            lock (ownershipSync)
            {
                if (nodeOwners.TryGetValue(node, out var owner))
                {
                    if (!ReferenceEquals(owner.Engine, this))
                        throw new FSMException($"Node {name}: owner conflict with {owner.Name}, engine {owner.Engine.executionIdentity}; requested engine {executionIdentity}.");
                }
                else nodeOwners.Add(node, new NodeOwner(this, name));
            }
        }

        internal void ValidateExecutionNodes()
        {
            var nodes = new HashSet<IFSMNode>(nodeDict.Values, FSMNodeIdentityComparer.Instance);
            foreach (var pair in nodeDict) ClaimNode(pair.Value, pair.Key);
            foreach (var node in nodes)
                foreach (var target in node.GetAllTargets())
                    if (!nodes.Contains(target))
                        throw new FSMException($"Node {node.Name}: target {target.Name} has owner {OwnerOf(target)?.executionIdentity ?? "unassembled"} outside requested engine {executionIdentity}.");
        }

        internal static FSMEngine? OwnerOf(IFSMNode node)
        {
            lock (ownershipSync)
                return nodeOwners.TryGetValue(node, out var owner) ? owner.Engine : null;
        }

        /// <summary>Diagnostic snapshot only; Prepare/start still acquire ownership atomically.</summary>
        public bool HasUnexitedExecution
        {
            get { lock (ExecutionSync) return executionOwner != null; }
        }

        internal object ExecutionSync { get; } = new object();
        private object? executionOwner;
        private Func<string>? executionState;

        internal void AcquireExecution(object owner)
        {
            lock (ExecutionSync)
            {
                if (executionOwner != null)
                    throw new FSMException($"Engine {executionIdentity} already owns unexited execution {RuntimeHelpers.GetHashCode(executionOwner)}, state {executionState?.Invoke() ?? "preparing/prepared"}; conflicting Prepare/start rejected.");
                executionOwner = owner;
            }
        }

        internal void DescribeExecution(object owner, Func<string> describeState)
        {
            lock (ExecutionSync)
                if (ReferenceEquals(executionOwner, owner)) executionState = describeState;
        }

        internal void ReleaseExecution(object owner)
        {
            lock (ExecutionSync)
                if (ReferenceEquals(executionOwner, owner))
                {
                    executionOwner = null;
                    executionState = null;
                }
        }
    }
}
