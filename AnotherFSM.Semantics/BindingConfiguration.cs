using System.Text.Json;

namespace AnotherFSM.Semantics;

/// <summary>Portable, user-authored exceptions. Paths are resolved against the configuration, never the IDE working directory.</summary>
public sealed record TypeTarget(string? Project, string? Assembly, string ClrName, string Alias, IReadOnlyList<TypeTarget> TypeArguments);
public sealed record ConfiguredMapping(string Key, TypeTarget Type, string Source);
public sealed record ModuleFile(string Name, string File, string Source);
public sealed record BindingOverride(KeyComparison Comparison, IReadOnlyList<ConfiguredMapping> Mappings,
    IReadOnlyList<ModuleFile> Modules, string? Error = null);

public sealed class BindingConfiguration
{
    private sealed record Context(string Host, string? Framework, IReadOnlyList<string> Scripts, BindingOverride Override);
    private readonly IReadOnlyList<Context> contexts;
    private readonly string? error;
    private BindingConfiguration(IReadOnlyList<Context> contexts, string? error = null)
    { this.contexts = contexts; this.error = error; }
    private static readonly BindingOverride Default = new(KeyComparison.OrdinalIgnoreCase, Array.Empty<ConfiguredMapping>(), Array.Empty<ModuleFile>());

    public static BindingConfiguration Parse(string configurationPath, string json)
    {
        try
        {
            var path = Path.GetFullPath(configurationPath);
            var directory = Path.GetDirectoryName(path)!;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) throw new FormatException("Unsupported configuration version");
            var contexts = new List<Context>();
            foreach (var item in root.GetProperty("contexts").EnumerateArray())
            {
                var source = path.Replace('\\', '/') + ": contexts[" + contexts.Count + "]";
                var host = Relative(directory, Required(item, "host"));
                var framework = Optional(item, "targetFramework");
                var scripts = item.TryGetProperty("scripts", out var files)
                    ? files.EnumerateArray().Select(file => Relative(directory, Nonempty(file.GetString()))).ToArray() : Array.Empty<string>();
                var comparison = Optional(item, "keyComparison") switch {
                    null or "OrdinalIgnoreCase" => KeyComparison.OrdinalIgnoreCase,
                    "Ordinal" => KeyComparison.Ordinal,
                    _ => throw new FormatException("keyComparison must be Ordinal or OrdinalIgnoreCase")
                };
                var mappings = new List<ConfiguredMapping>();
                if (item.TryGetProperty("mappings", out var entries))
                    foreach (var entry in entries.EnumerateArray())
                        mappings.Add(new(Required(entry, "key"), Target(entry.GetProperty("type"), directory), source + ".mappings[" + mappings.Count + "]"));
                var modules = new List<ModuleFile>();
                if (item.TryGetProperty("modules", out var registered))
                    foreach (var entry in registered.EnumerateArray())
                        modules.Add(new(Required(entry, "name"), Relative(directory, Required(entry, "file")), source + ".modules[" + modules.Count + "]"));
                contexts.Add(new(host, framework, scripts, new(comparison, mappings, modules)));
            }
            return new(contexts);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or NotSupportedException or OverflowException)
        {
            return new(Array.Empty<Context>(), configurationPath + ": invalid configuration: " + exception.Message);
        }
    }

    public BindingOverride Select(string hostProject, string framework, string script)
    {
        if (error != null) return Default with { Error = error };
        var applicable = contexts.Where(context => SamePath(context.Host, hostProject)
            && (context.Framework == null || context.Framework == framework)
            && (context.Scripts.Count == 0 || context.Scripts.Any(path => SamePath(path, script)))).ToArray();
        if (applicable.Length == 0) return Default;
        if (applicable.Select(context => context.Override.Comparison).Distinct().Count() > 1)
            return Default with { Error = "Conflicting keyComparison rules in applicable anotherfsm.json contexts" };
        return new(applicable[0].Override.Comparison, applicable.SelectMany(context => context.Override.Mappings).ToArray(),
            applicable.SelectMany(context => context.Override.Modules).ToArray());
    }

    private static TypeTarget Target(JsonElement item, string directory)
    {
        var project = Optional(item, "project");
        var assembly = Optional(item, "assembly");
        if ((project == null) == (assembly == null)) throw new FormatException("A type target requires exactly one project or full assembly identity");
        return new(project == null ? null : Relative(directory, project), assembly, Required(item, "clrName"),
            Optional(item, "alias") ?? "global", item.TryGetProperty("typeArguments", out var arguments)
                ? arguments.EnumerateArray().Select(argument => Target(argument, directory)).ToArray() : Array.Empty<TypeTarget>());
    }
    private static string Required(JsonElement item, string name) => Nonempty(item.GetProperty(name).GetString());
    private static string? Optional(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? Nonempty(value.GetString()) : null;
    private static string Nonempty(string? value) => !string.IsNullOrWhiteSpace(value) ? value : throw new FormatException("Nonempty string required");
    private static string Relative(string directory, string path) => Path.GetFullPath(path, directory).Replace('\\', '/');
    private static bool SamePath(string left, string right) => StringComparer.OrdinalIgnoreCase.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'));
}
