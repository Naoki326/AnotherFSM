namespace AnotherFSM.Syntax
{
    // Source-linked into the strict runtime, declaration generator and editor core.
    // Keeping these pure avoids making runtime packages depend on an IDE assembly.
    internal static class DeclarationRules
    {
        internal static string Qualify(string prefix, string name) => prefix + name;
        internal static string TypeKey(string name, string? explicitKey) => explicitKey ?? name;
        internal static bool IsRemovedComposition(string type)
        {
            switch (type.ToUpperInvariant())
            {
                case "GROUP": case "GROUPT": case "GROUPTU":
                case "PARALLEL": case "PARALLELT": case "PARALLELTU":
                case "GROUPNODE": case "PARALLELNODE": return true;
                default: return false;
            }
        }
    }
}
