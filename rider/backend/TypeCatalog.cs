using System.Text.Json;
using AnotherFSM.Semantics;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.Tree;

namespace AnotherFSM.Rider;

/// <summary>Reads committed host PSI only. Never loads an assembly or constructs an attribute.</summary>
public static class TypeCatalog
{
    public static TypeIdentity Identity(ITypeElement type, IPsiModule? consumer = null, string alias = "global", string construction = "")
        => new(type.Module.GetPersistentID(), (consumer ?? type.Module).TargetFrameworkId.UniqueString,
            type.GetClrName().FullName, consumer?.GetPersistentID() ?? "", construction, alias);
    public static bool SameDefinition(TypeIdentity left, TypeIdentity right) => left.Module == right.Module && left.ClrName == right.ClrName;

    public static IReadOnlyList<NodeType> Read(IPsiModule module)
    {
        if (!module.IsValid()) return Array.Empty<NodeType>();
        using var context = CompilationContextCookie.GetOrCreate(module.GetContextFromModule());
        var symbols = module.GetPsiServices().Symbols;
        var scope = symbols.GetSymbolScope(module, true, true);
        var contracts = ConventionTypes(module, "StateMachine.IFSMNode");
        var attributes = ConventionTypes(module, "StateMachine.FSMNodeAttribute");
        if (contracts.Length != 1 || attributes.Length != 1
            || !ReferenceEquals(contracts[0].Module, attributes[0].Module)) return Array.Empty<NodeType>();
        var contract = contracts[0];
        var attribute = attributes[0];
        var result = new Dictionary<TypeIdentity, NodeType>();
        foreach (var type in scope.GetAllTypeElementsGroupedByName())
        {
            JetBrains.Application.Interruption.Current.CheckAndThrow();
            if (type is not IClass klass || klass.IsAbstract || !type.IsValid() || !Accessible(type, module)) continue;
            var aliases = Aliases(type, module);
            if (aliases.Length == 0) continue;
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
            var identity = Identity(type, module, string.Join(",", aliases));
            result[identity] = new NodeType(identity, key, Implements(type, contract), OpenDefinition(type));
        }
        return result.Values.ToArray();
    }

    private static bool OpenDefinition(ITypeElement type)
    {
        for (var current = type; current != null; current = current.GetContainingType())
            if (current.HasTypeParameters()) return true;
        return false;
    }
    private sealed class ConsumerAccess(IPsiModule module) : IAccessContext
    {
        public ITypeElement GetAccessContainingTypeElement() => null!;
        public Staticness GetStaticness() => Staticness.Any;
        public QualifierKind GetQualifierKind() => QualifierKind.NONE;
        public ITypeElement GetQualifierTypeElement() => null!;
        public IPsiModule GetPsiModule() => module;
        public IPsiSourceFile GetSourceFile() => null!;
    }
    private static bool Accessible(ITypeElement type, IPsiModule consumer)
    {
        for (var current = type; current != null; current = current.GetContainingType())
            if (current is ITypeMember member && !AccessUtil.IsSymbolAccessible(member, new ConsumerAccess(consumer))) return false;
        return true;
    }
    private static string[] Aliases(ITypeElement type, IPsiModule consumer)
    {
        if (ReferenceEquals(type.Module, consumer)) return new[] { "global" };
        return consumer.GetPsiServices().Modules.GetModuleReferences(consumer, consumer.GetContextFromModule())
            .Where(reference => ReferenceEquals(reference.Module, type.Module))
            .SelectMany(reference => reference.ExternAliases.Length == 0 ? new[] { "global" } : reference.ExternAliases)
            .Distinct().Order(StringComparer.Ordinal).ToArray();
    }
    private static ITypeElement[] ConventionTypes(IPsiModule consumer, string clrName)
    {
        var candidates = consumer.GetPsiServices().Symbols.GetSymbolScope(consumer, true, true).GetTypeElementsByCLRName(clrName)
            .Where(type => type.IsValid() && Aliases(type, consumer).Length > 0).ToArray();
        var global = candidates.Where(type => Aliases(type, consumer).Contains("global", StringComparer.Ordinal)).ToArray();
        // Alias-only names do not collide with the default global convention. A unique alias-only
        // contract remains usable when there is no global contract; multiple alternatives stay unresolved.
        return global.Length > 0 ? global : candidates;
    }
    public static BindingOptions Options(IPsiModule consumer, BindingOverride configuration)
    {
        if (!consumer.IsValid()) return new(configuration.Comparison, Array.Empty<ExplicitNodeMapping>(), "Consuming host unavailable");
        using var context = CompilationContextCookie.GetOrCreate(consumer.GetContextFromModule());
        var contracts = ConventionTypes(consumer, "StateMachine.IFSMNode");
        if (contracts.Length != 1) return new(configuration.Comparison, Array.Empty<ExplicitNodeMapping>(),
            configuration.Error ?? "IFSMNode contract is unavailable or ambiguous in this consuming context; no determined binding");
        var mappings = configuration.Mappings.Select(mapping => {
            var target = ResolveTarget(consumer, mapping.Type);
            var node = target?.GetTypeElement();
            var concrete = target != null && !target.IsOpenType && target.IsWellFormed(new CSharpTypeConstraintsVerifier(consumer));
            var candidates = node == null ? Array.Empty<NodeType>() : new[] { new NodeType(
                Identity(node, consumer, mapping.Type.Alias, JsonSerializer.Serialize(mapping.Type)), null,
                node is IClass klass && !klass.IsAbstract && Implements(node, contracts[0]), !concrete) };
            return new ExplicitNodeMapping(mapping.Key, candidates, mapping.Source + " → " + JsonSerializer.Serialize(mapping.Type));
        }).ToArray();
        return new(configuration.Comparison, mappings, configuration.Error);
    }
    private static IDeclaredType? ResolveTarget(IPsiModule consumer, TypeTarget target)
    {
        JetBrains.Application.Interruption.Current.CheckAndThrow();
        var matches = consumer.GetPsiServices().Symbols.GetSymbolScope(consumer, true, true).GetTypeElementsByCLRName(target.ClrName)
            .Where(type => type.IsValid() && Accessible(type, consumer) && Aliases(type, consumer).Contains(target.Alias, StringComparer.Ordinal)
                && (target.Project != null ? StringComparer.OrdinalIgnoreCase.Equals(BindingSettings.ProjectPath(type.Module), target.Project)
                    : type.Module is IAssemblyPsiModule assembly && assembly.Assembly.AssemblyName.FullName == target.Assembly)).ToArray();
        if (matches.Length != 1) return null;
        var arity = 0;
        for (var owner = matches[0]; owner != null; owner = owner.GetContainingType()) arity += owner.TypeParameters.Count;
        if (target.TypeArguments.Count != arity) return null;
        var arguments = target.TypeArguments.Select(argument => ResolveTarget(consumer, argument)).ToArray();
        if (arguments.Any(argument => argument == null || argument.IsOpenType)) return null;
        // CreateType consumes outer type arguments before inner type arguments, preserving nested construction.
        var constructed = TypeFactory.CreateType(matches[0], arguments.Cast<IType>().ToArray());
        return constructed.IsWellFormed(new CSharpTypeConstraintsVerifier(consumer)) ? constructed : null;
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
        if (type == null || type is ICompiledElement || type.Module.ContainingProjectModule is not IProject) return null;
        var sourceContext = type.GetSourceFiles().FirstOrDefault(file => file.IsValid());
        if (sourceContext == null) return null;
        // C# constant/collection conversion consults runtime features from the active compilation context.
        using var context = CompilationContextCookie.GetOrCreate(sourceContext.ResolveContext);
        var attributes = ConventionTypes(type.Module, "StateMachine.FSMNodeAttribute");
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
        var consumer = psi.Modules.GetById(identity.Consumer.Length == 0 ? identity.Module : identity.Consumer);
        if (consumer == null || !consumer.IsValid() || consumer.TargetFrameworkId.UniqueString != identity.Context) return null;
        using var context = CompilationContextCookie.GetOrCreate(consumer.GetContextFromModule());
        if (identity.Construction.Length > 0)
        {
            TypeTarget? target;
            try { target = JsonSerializer.Deserialize<TypeTarget>(identity.Construction); }
            catch (JsonException) { return null; }
            var constructed = target == null ? null : ResolveTarget(consumer, target);
            var element = constructed?.GetTypeElement();
            return element != null && !constructed!.IsOpenType && Identity(element, consumer, target!.Alias, identity.Construction) == identity ? element : null;
        }
        var types = psi.Symbols.GetSymbolScope(consumer, true, true).GetTypeElementsByCLRName(identity.ClrName)
            .Where(type => type.IsValid() && type.Module.GetPersistentID() == identity.Module && Accessible(type, consumer)
                && identity.Alias.Split(',').All(alias => Aliases(type, consumer).Contains(alias, StringComparer.Ordinal))).ToArray();
        return types.Length == 1 ? types[0] : null;
    }
}
