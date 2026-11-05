namespace StateMachine
{
    public partial class FSMEngine
    {
        // One validator for C#, flattened modules and future host/factory entry points.
        internal void ValidateContextNodes()
        {
            foreach (var pair in nodeDict)
            {
                var node = pair.Value;
                if (node is not IFSMContextNode actual)
                    throw new FSMException($"Node {pair.Key}, key {node.ClassType}, actual {node.GetType()}: no explicit business dependency declaration.");
                var actualRequirement = DeclaredContextType(node.GetType(), pair.Key, node.ClassType);
                Type declaredType;
                try { declaredType = nodeFactory.GetNodeType(node.ClassType); }
                catch (Exception ex)
                {
                    throw new FSMException($"Node {pair.Key}, key {node.ClassType}, actual {node.GetType()}, requirement {actualRequirement}: factory metadata failed: {ex.Message}", ex);
                }
                if (declaredType == null || !typeof(IFSMNode).IsAssignableFrom(declaredType)
                    || !typeof(IFSMContextNode).IsAssignableFrom(declaredType))
                    throw new FSMException($"Node {pair.Key}, key {node.ClassType}: invalid metadata declared {declaredType?.ToString() ?? "null"}; actual {node.GetType()}, requirement {actualRequirement}; source not yet selected.");
                var declaredRequirement = DeclaredContextType(declaredType, pair.Key, node.ClassType);
                if (!declaredType.IsInstanceOfType(node) || declaredRequirement != actualRequirement
                    || actual.ContextType != actualRequirement
                    || actualRequirement != null && node is not IFSMContextBindingTarget)
                    throw new FSMException($"Node {pair.Key}, key {node.ClassType}: metadata declared {declaredType}, requirement {declaredRequirement}; actual {node.GetType()}, requirement {actualRequirement}, reported {actual.ContextType}.");
            }
        }

        private static Type? DeclaredContextType(Type nodeType, string qualifiedName, string typeKey)
        {
            var requirements = nodeType.GetInterfaces().Where(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IFSMContextNode<>)).Select(i => i.GetGenericArguments()[0]).Distinct().ToArray();
            if (requirements.Length > 1)
                throw new FSMException($"Node {qualifiedName}, key {typeKey}, type {nodeType} has inconsistent multiple context requirements: {string.Join(", ", requirements.Select(t => t.ToString()))}.");
            return requirements.SingleOrDefault();
        }
    }
}
