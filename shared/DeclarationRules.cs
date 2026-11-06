namespace AnotherFSM.Syntax
{
    // Source-linked into the strict runtime, declaration generator and editor core.
    // Keeping these pure avoids making runtime packages depend on an IDE assembly.
    internal static class DeclarationRules
    {
        internal static System.StringComparer ModuleComparer => System.StringComparer.OrdinalIgnoreCase;
        internal static System.StringComparer NameComparer => System.StringComparer.Ordinal;
        internal static string ModuleRegistrationName(string name, string? alias) => alias ?? name;
        internal static bool IsQualifiedInterface(string reference, string instance, System.Collections.Generic.IEnumerable<string> names)
            => reference.StartsWith(instance + ".", System.StringComparison.OrdinalIgnoreCase)
                && System.Linq.Enumerable.Contains(names, reference.Substring(instance.Length + 1), NameComparer);
        internal static string QualifyNodeReference(string prefix, string reference,
            System.Func<string, bool> isInstanceOrNode, System.Func<string, bool> isInstanceOrExpandedNode, System.Func<string, bool> hasOwner)
        {
            var local = Qualify(prefix, reference);
            if (isInstanceOrNode(local)) return local;
            if (isInstanceOrExpandedNode(reference)) return reference;
            if (hasOwner(local)) return local;
            return hasOwner(reference) ? reference : local;
        }
        internal static int BranchIndex(string text)
        {
            switch (text)
            {
                case "none": return 0;
                case "success": return 1;
                case "failed": return 2;
                case "error": return 3;
                case "break": return 4;
                case "cancel": return 5;
                default: return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
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
