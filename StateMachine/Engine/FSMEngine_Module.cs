namespace StateMachine;

public partial class FSMEngine
{
    internal Dictionary<string, FsmModuleInfo> moduleRegistry = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, FsmModuleInstance> moduleInstances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hosts without a filesystem can reject external module discovery explicitly.</summary>
    public bool AllowExternalModuleFiles { get; set; } = true;

    public IReadOnlyDictionary<string, FsmModuleInstance> ModuleInstances => moduleInstances;
    public FsmModuleInterfaceResolver ModuleInterfaces => new(moduleInstances, nodeDict, eventDict);

    // 外部编辑工具使用的模型入口；不创建运行时模块节点。
    public void AddModuleInstance(string instanceName, FsmModuleInstance instance)
        => moduleInstances[instanceName] = instance;

    public bool RemoveModuleInstance(string instanceName) => moduleInstances.Remove(instanceName);

    public void RegisterModule(string moduleName, string scriptContent, string? directoryPath = null)
    {
        if (moduleRegistry.ContainsKey(moduleName)) return;
        moduleRegistry.Add(moduleName, new FsmModuleInfo
        {
            ModuleName = moduleName, ScriptContent = scriptContent, DirectoryPath = directoryPath,
        });
    }

    public void RegisterModuleByFile(string moduleName, string filePath)
    {
        EnsureExternalModuleFilesAllowed(moduleName);
        RegisterModule(moduleName, File.ReadAllText(filePath), Path.GetDirectoryName(filePath));
    }

    private void EnsureExternalModuleFilesAllowed(string moduleName)
    {
        if (!AllowExternalModuleFiles)
            throw new ScriptException($"Browser runtime: external module files are not supported ('{moduleName}'); include the Module definition in the script.");
    }

    public bool TryGetModule(string moduleName, out FsmModuleInfo moduleInfo)
        => moduleRegistry.TryGetValue(moduleName, out moduleInfo);
    public bool ContainsModule(string moduleName) => moduleRegistry.ContainsKey(moduleName);
    public IEnumerable<string> GetModuleNames() => moduleRegistry.Keys;

    internal void LoadModuleWithDependencies(string moduleName, string directoryPath, HashSet<string> loadingStack)
    {
        if (!loadingStack.Add(moduleName))
            throw new ScriptException($"Circular module dependency detected: {moduleName}");
        try
        {
            if (!moduleRegistry.TryGetValue(moduleName, out var info))
            {
                EnsureExternalModuleFilesAllowed(moduleName);
                var path = Path.Combine(directoryPath, moduleName + ".fsm");
                if (!File.Exists(path)) throw new ScriptException($"Module file not found: {path}");
                RegisterModuleByFile(moduleName, path);
                info = moduleRegistry[moduleName];
            }
            if (!info.IsParsed) ParseModuleContent(info, loadingStack);
        }
        finally
        {
            // 这是当前递归路径，不是所有已访问模块的集合；重复/菱形依赖不是循环。
            loadingStack.Remove(moduleName);
        }
    }

    internal void ParseModuleContent(FsmModuleInfo info, HashSet<string>? loadingStack = null)
    {
        var nodes = new Dictionary<string, IFSMNode>();
        var events = new Dictionary<string, FSMEvent>();
        var tree = FsmScriptParser.Parse(info.ScriptContent);
        // 模块文件中的 import 与主脚本共享同一个递归栈和相对目录规则。
        var visitor = new BuildStateVisitor(events, nodes, nodeFactory, this, loadingStack, info.DirectoryPath, info);
        visitor.Visit(tree);
        var registered = moduleRegistry[info.ModuleName];
        if (ReferenceEquals(registered, info))
        {
            info.TemplateNodes = nodes;
            info.TemplateEvents = events;
            info.IsParsed = true;
        }
        else
        {
            registered.ScriptContent = info.ScriptContent;
        }
    }
}
