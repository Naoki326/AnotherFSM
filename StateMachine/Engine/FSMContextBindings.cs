using System.Linq.Expressions;
using System.Reflection;

namespace StateMachine
{
    /// <summary>Reusable type-level relationships. Expressions select existing source references, never execution instances.</summary>
    public sealed class FSMContextBindings<TInput> where TInput : class
    {
        internal readonly List<(string? Node, Type? Requirement, LambdaExpression Source)> Rules = new();

        public void Default<TContext>(Expression<Func<TInput, TContext>> source) where TContext : class
            => Rules.Add((null, typeof(TContext), source));
        public void DefaultSource<TSource>(Expression<Func<TInput, TSource>> source) where TSource : class
            => Rules.Add((null, null, source));
        public void Node<TContext>(string qualifiedName, Expression<Func<TInput, TContext>> source) where TContext : class
        {
            if (string.IsNullOrWhiteSpace(qualifiedName))
                throw new FSMException($"Node qualified name is required for requirement {typeof(TContext)}, Source {source}.");
            Rules.Add((qualifiedName, typeof(TContext), source));
        }
    }

    internal sealed class ContextSource : IEquatable<ContextSource>
    {
        private readonly (Module Module, int Token, Type? DeclaringType)[] identity;
        internal PropertyInfo[] Properties { get; }
        internal string Path => string.Join(".", Properties.Select(p => p.Name));
        internal Type DeclaredType => Properties.Last().PropertyType;
        internal ContextSource(params PropertyInfo[] properties)
        {
            Properties = properties;
            // ReflectedType can differ between automatic scanning and expressions for the
            // same inherited property. Keep the constructed DeclaringType for generics.
            identity = properties.Select(p => (p.Module, p.MetadataToken, p.DeclaringType)).ToArray();
        }
        public bool Equals(ContextSource? other) => other != null && identity.SequenceEqual(other.identity);
        public override bool Equals(object? obj) => obj is ContextSource other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var member in identity) hash = hash * 31 + member.GetHashCode();
                return hash;
            }
        }
        internal static ContextSource Parse(LambdaExpression expression)
        {
            if (expression == null) throw new FSMException("Source expression is null.");
            var properties = new List<PropertyInfo>();
            Expression? current = expression.Body;
            while (current is MemberExpression member && member.Member is PropertyInfo property)
            {
                properties.Insert(0, property);
                current = member.Expression;
            }
            if (current != expression.Parameters[0] || properties.Count == 0)
                throw new FSMException($"Source {expression}: only existing property paths are allowed (no conversions, calls, selection or projection).");
            return new ContextSource(properties.ToArray());
        }
        internal void Validate()
        {
            if (DeclaredType.IsValueType)
                throw new FSMException($"Source {this} must have a reference declaration type; boxing would copy business data.");
            foreach (var property in Properties)
            {
                if (property.GetMethod?.IsPublic != true || property.GetMethod.IsStatic || property.GetIndexParameters().Length != 0)
                    throw new FSMException($"Source {this}: {property.Name} must be public readable, non-static and non-indexed.");
                var setter = property.SetMethod;
                if (setter?.IsPublic == true && !setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
                    throw new FSMException($"Source {this}: {property.Name} has ordinary public set; use init, private set or a getter-only property.");
            }
        }
        internal object? Resolve(object input)
        {
            object? value = input;
            foreach (var property in Properties)
                value = value == null ? null : property.GetValue(value);
            return value;
        }
        public override string ToString() => $"{Path} (declared {DeclaredType})";
    }
}
