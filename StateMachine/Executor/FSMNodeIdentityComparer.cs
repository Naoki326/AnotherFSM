using System.Runtime.CompilerServices;

namespace StateMachine
{
    internal sealed class FSMNodeIdentityComparer : IEqualityComparer<IFSMNode>
    {
        internal static readonly FSMNodeIdentityComparer Instance = new();
        public bool Equals(IFSMNode? x, IFSMNode? y) => ReferenceEquals(x, y);
        public int GetHashCode(IFSMNode obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
