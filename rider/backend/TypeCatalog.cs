using AnotherFSM.Semantics;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Modules;

namespace AnotherFSM.Rider;

/// <summary>Reads committed host PSI only. Never loads an assembly or constructs an attribute.</summary>
public static class TypeCatalog
{
    public static TypeIdentity Identity(ITypeElement type) => new(type.Module.GetPersistentID(),
        type.Module.TargetFrameworkId.UniqueString, type.GetClrName().FullName);

    public static IReadOnlyList<NodeType> Read(IPsiModule module)
    {
        if (!module.IsValid()) return Array.Empty<NodeType>();
        var symbols = module.GetPsiServices().Symbols;
        var scope = symbols.GetSymbolScope(module, true, true);
        var contracts = scope.GetTypeElementsByCLRName("StateMachine.IFSMNode");
        var attributes = scope.GetTypeElementsByCLRName("StateMachine.FSMNodeAttribute");
        if (contracts.Length != 1 || attributes.Length != 1
            || !ReferenceEquals(contracts[0].Module, attributes[0].Module)) return Array.Empty<NodeType>();
        var contract = contracts[0];
        var attribute = attributes[0];
        var result = new Dictionary<TypeIdentity, NodeType>();
        foreach (var file in module.SourceFiles.Where(f => f.IsValid() && f.PrimaryPsiLanguage is CSharpLanguage))
        {
            JetBrains.Application.Interruption.Current.CheckAndThrow();
            using var context = CompilationContextCookie.GetOrCreate(file.ResolveContext);
            foreach (var type in symbols.GetTypesAndNamespacesInFile(file).OfType<ITypeElement>())
            {
                JetBrains.Application.Interruption.Current.CheckAndThrow();
                if (type is not IClass klass || klass.IsAbstract || !type.IsValid()
                    || type.Module.GetPersistentID() != module.GetPersistentID()) continue;
                var instances = type.GetAttributeInstances(AttributesSource.Self).Where(instance => {
                    var resolved = instance.GetAttributeType().GetTypeElement();
                    return resolved != null && resolved.GetClrName().FullName == attribute.GetClrName().FullName
                        && ReferenceEquals(resolved.Module, attribute.Module);
                }).ToArray();
                if (instances.Length != 1) continue;
                var instance = instances[0];
                var named = instance.NamedParameters().Where(pair => pair.First == "Key").ToArray();
                // A present but invalid named value must not fall back to the constructor argument.
                var value = named.Length == 1 ? named[0].Second : named.Length > 1 ? AttributeValue.BAD_VALUE
                    : instance.PositionParameter(0);
                var key = !value.IsBadValue && value.IsConstant && value.ConstantValue.IsNotNullString(out var text) ? text : null;
                result[Identity(type)] = new NodeType(Identity(type), key, Implements(type, contract), type.HasTypeParameters());
            }
        }
        return result.Values.ToArray();
    }

    private static bool Implements(ITypeElement type, ITypeElement contract)
    {
        var pending = new Stack<ITypeElement>();
        var visited = new HashSet<TypeIdentity>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            JetBrains.Application.Interruption.Current.CheckAndThrow();
            if (!visited.Add(Identity(current))) continue;
            if (current.GetClrName().FullName == contract.GetClrName().FullName
                && ReferenceEquals(current.Module, contract.Module)) return true;
            foreach (var parent in current.GetSuperTypes())
                if (parent.GetTypeElement() is { } element) pending.Push(element);
        }
        return false;
    }

    public static ITypeElement? Resolve(IPsiServices psi, TypeIdentity identity)
    {
        var module = psi.Modules.GetById(identity.Module);
        if (module == null || !module.IsValid() || module.TargetFrameworkId.UniqueString != identity.Context) return null;
        var types = psi.Symbols.GetSymbolScope(module, false, true).GetTypeElementsByCLRName(identity.ClrName)
            .Where(t => t.IsValid() && Identity(t) == identity).ToArray();
        return types.Length == 1 ? types[0] : null;
    }
}
