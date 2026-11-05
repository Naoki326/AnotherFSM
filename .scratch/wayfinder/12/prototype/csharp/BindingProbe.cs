// THROWAWAY: synchronous binding probe, NOT AnotherFSM or a dependency container.
using System.Linq.Expressions;
using System.Reflection;

namespace Throwaway;

public interface IQueryContext { List<string> Tasks { get; } }
public interface IProcessContext { List<string> Tasks { get; } }
public sealed class JobData(string label) : IQueryContext, IProcessContext
{
    public string Label { get; } = label;
    public List<string> Tasks { get; } = [];
    public override string ToString() => $"{Label} Tasks=[{string.Join(",", Tasks)}]";
}

public abstract class ProbeNode(string name, Type? requirement)
{
    public string Name { get; } = name;
    public Type? Requirement { get; } = requirement;
    public static int Calls { get; private set; }
    internal void Invoke(object? value) { Calls++; Execute(value); }
    protected abstract void Execute(object? value);
}
public abstract class ContextNode<T>(string name) : ProbeNode(name, typeof(T)) where T : class
{
    protected T Context { get; private set; } = null!;
    protected sealed override void Execute(object? value)
    {
        Context = (T)value!; // Internal erasure only; no caller casts.
        try { Execute(); }
        finally { Context = null!; }
    }
    protected abstract void Execute();
}
public sealed class QueryNode(string name, Action<IQueryContext> observe) : ContextNode<IQueryContext>(name)
{
    protected override void Execute()
    {
        Console.WriteLine($"NODE {Name} IQueryContext: {Context}");
        observe(Context);
    }
}
public sealed class ProcessNode(string name, Action<IProcessContext> after) : ContextNode<IProcessContext>(name)
{
    protected override void Execute()
    {
        Context.Tasks.Add("processed");
        Console.WriteLine($"NODE {Name} IProcessContext: {Context}");
        after(Context);
    }
}
public sealed class NoBusinessNode(string name) : ProbeNode(name, null)
{
    protected override void Execute(object? value) => Console.WriteLine($"NODE {Name}: no business binding");
}

public sealed class Bindings<TAggregate> where TAggregate : class
{
    internal sealed record Source(PropertyInfo Property)
    {
        public string Path => Property.Name;
        public Type DeclaredType => Property.PropertyType;
        public object? Read(TAggregate aggregate) => Property.GetValue(aggregate);
    }
    private Source? defaultSource;
    private readonly Dictionary<Type, Source> defaults = [];
    private readonly Dictionary<string, Source> overrides = [];

    // Generic return types make incompatible expressions actual C# compile errors.
    public void Default<TContext>(Expression<Func<TAggregate, TContext>> expression) where TContext : class
        => defaults[typeof(TContext)] = Select(expression);
    public void Node<TContext>(string name, Expression<Func<TAggregate, TContext>> expression) where TContext : class
        => overrides[name] = Select(expression);
    public void DefaultSource<TSource>(Expression<Func<TAggregate, TSource>> expression) where TSource : class
        => defaultSource = Select(expression);

    private static Source Select(LambdaExpression expression)
    {
        Expression body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } convert) body = convert.Operand;
        // Deliberately narrow: direct properties only. This is NOT a full expression validator.
        if (body is MemberExpression { Member: PropertyInfo p, Expression: ParameterExpression }) return new(p);
        throw new NotSupportedException($"Probe supports direct property selection only: {expression}");
    }
    internal Source Resolve(ProbeNode node)
    {
        var source = Choose(node);
        var setter = source.Property.SetMethod;
        bool isInit = setter?.ReturnParameter.GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true;
        if (setter is { IsPublic: true } && !isInit)
            throw new InvalidOperationException($"ASSEMBLY {typeof(TAggregate).Name} / {node.Name} / {node.Requirement!.Name}: " +
                $"source {source.Path}:{source.DeclaredType.Name} has ordinary public set; use init/private set/getter-only");
        return source;
    }
    private Source Choose(ProbeNode node)
    {
        var need = node.Requirement!;
        if (overrides.TryGetValue(node.Name, out var chosen)) return chosen;
        if (defaults.TryGetValue(need, out chosen)) return chosen;
        if (defaultSource is not null && need.IsAssignableFrom(defaultSource.DeclaredType)) return defaultSource;
        var candidates = typeof(TAggregate).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true, IsStatic: false }
                && p.GetIndexParameters().Length == 0 && need.IsAssignableFrom(p.PropertyType))
            .Select(p => new Source(p)).ToArray();
        if (candidates.Length == 1) return candidates[0];
        string detail = candidates.Length == 0 ? "no compatible source" : "ambiguous sources";
        throw new InvalidOperationException($"ASSEMBLY {typeof(TAggregate).Name} / {node.Name} / {need.Name}: {detail}; " +
            string.Join(", ", candidates.Select(s => $"{s.Path}:{s.DeclaredType.Name}")));
    }
}

public sealed class ProbeFlow<TAggregate> where TAggregate : class
{
    private readonly (ProbeNode Node, Bindings<TAggregate>.Source? Source)[] plan;
    public ProbeFlow(Bindings<TAggregate> bindings, params ProbeNode[] nodes)
    {
        plan = nodes.Select(n => (n, n.Requirement is null ? null : bindings.Resolve(n))).ToArray();
        foreach (var (node, source) in plan)
            Console.WriteLine($"PLAN {node.Name} / {node.Requirement?.Name ?? "none"} <- {source?.Path ?? "(no binding)"}");
    }
    public void Run(TAggregate aggregate)
    {
        if (aggregate is null) throw new InvalidOperationException($"START {typeof(TAggregate).Name}: aggregate is null");
        var values = new Dictionary<PropertyInfo, object?>();
        // Resolve the ENTIRE supplied flat plan before invoking even its first node.
        foreach (var (_, source) in plan)
            if (source is not null && !values.ContainsKey(source.Property)) values.Add(source.Property, source.Read(aggregate));
        foreach (var (node, source) in plan)
            if (source is not null && values[source.Property] is null)
            {
                var affected = plan.Where(p => p.Source?.Property == source.Property)
                    .Select(p => $"{p.Node.Name}/{p.Node.Requirement!.Name}");
                throw new InvalidOperationException($"START {typeof(TAggregate).Name}: {source.Path}:{source.DeclaredType.Name} is null; " +
                    $"affected={string.Join(", ", affected)}");
            }
        foreach (var (node, source) in plan) node.Invoke(source is null ? null : values[source.Property]);
    }
}
