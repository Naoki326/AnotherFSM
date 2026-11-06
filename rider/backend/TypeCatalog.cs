using AnotherFSM.Semantics;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.Tree;

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

    public sealed record BranchMetadataLocation(string DocumentId, SourceSpan Span, string Text);

    /// <summary>Only explicit, statically known FSMNode.Indexes metadata; never enum names or return/yield analysis.</summary>
    public static BranchMetadataLocation? BranchMetadata(IPsiServices psi, TypeIdentity identity, int index)
    {
        var type = Resolve(psi, identity);
        if (type == null) return null;
        var sourceContext = type.GetSourceFiles().FirstOrDefault(file => file.IsValid());
        if (sourceContext == null) return null;
        // C# constant/collection conversion consults runtime features from the active compilation context.
        using var context = CompilationContextCookie.GetOrCreate(sourceContext.ResolveContext);
        var attributes = psi.Symbols.GetSymbolScope(type.Module, true, true).GetTypeElementsByCLRName("StateMachine.FSMNodeAttribute");
        if (attributes.Length != 1) return null;
        var attributeType = attributes[0];
        var instances = type.GetAttributeInstances(AttributesSource.Self).Where(instance => {
            var resolved = instance.GetAttributeType().GetTypeElement();
            return resolved != null && resolved.GetClrName().FullName == attributeType.GetClrName().FullName
                && ReferenceEquals(resolved.Module, attributeType.Module);
        }).ToArray();
        if (instances.Length != 1) return null;
        var instance = instances[0];
        var named = instance.NamedParameters().Where(pair => pair.First == "Indexes").ToArray();
        var value = AttributeValue.BAD_VALUE;
        if (named.Length == 1) value = named[0].Second;
        else if (named.Length == 0 && instance.Constructor is { } constructor)
        {
            var parameters = constructor.Parameters.ToArray();
            var positions = Enumerable.Range(0, parameters.Length).Where(i => parameters[i].ShortName == "indexes").ToArray();
            if (positions.Length == 1 && positions[0] < instance.PositionParameterCount) value = instance.PositionParameter(positions[0]);
        }
        if (!value.IsArray || value.ArrayValue == null
            || value.ArrayValue.Any(item => !item.IsConstant || item.ConstantValue.Kind != ConstantValueKind.Int)
            || !value.ArrayValue.Any(item => item.ConstantValue.IntValue == index)) return null;
        var declarations = type.GetDeclarations().OfType<IAttributesOwnerDeclaration>().SelectMany(declaration => declaration.Attributes)
            .Where(attribute => attribute.TypeReference?.Resolve().DeclaredElement is ITypeElement resolved
                && resolved.GetClrName().FullName == attributeType.GetClrName().FullName && ReferenceEquals(resolved.Module, attributeType.Module)).ToArray();
        if (declarations.Length != 1) return null;
        var source = declarations[0].GetSourceFile();
        var range = declarations[0].GetDocumentRange();
        if (source == null || !source.IsValid() || !range.IsValid()) return null;
        return new(ScriptDocuments.Id(source), new(range.TextRange.StartOffset, range.TextRange.Length), source.Document.GetText());
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
