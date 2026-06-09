using System.IO;
using System.Text.RegularExpressions;
using Antlr4.Runtime;

namespace StateMachine
{
    public partial class FSMEngine
    {
        internal Dictionary<string, FsmModuleInfo> moduleRegistry = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, FsmModuleInstance> moduleInstances = new(StringComparer.OrdinalIgnoreCase);

        // 公开只读访问器（供测试和外部工具使用）
        public IReadOnlyDictionary<string, FsmModuleInstance> ModuleInstances => moduleInstances;

        // 添加模块实例（供外部工具在运行时动态创建模块实例）
        public void AddModuleInstance(string instanceName, FsmModuleInstance instance)
        {
            moduleInstances[instanceName] = instance;
        }

        // 移除模块实例（供外部工具在运行时删除模块实例）
        public bool RemoveModuleInstance(string instanceName)
        {
            return moduleInstances.Remove(instanceName);
        }

        // 注册模块（脚本内容），重复注册自动跳过
        public void RegisterModule(string moduleName, string scriptContent, string? directoryPath = null)
        {
            if (moduleRegistry.ContainsKey(moduleName))
                return;
            moduleRegistry[moduleName] = new FsmModuleInfo { ModuleName = moduleName, ScriptContent = scriptContent, DirectoryPath = directoryPath };
        }

        // 注册模块（文件路径）
        public void RegisterModuleByFile(string moduleName, string filePath)
        {
            var content = File.ReadAllText(filePath);
            RegisterModule(moduleName, content, Path.GetDirectoryName(filePath));
        }

        // 查询
        public bool TryGetModule(string moduleName, out FsmModuleInfo moduleInfo) => moduleRegistry.TryGetValue(moduleName, out moduleInfo);
        public bool ContainsModule(string moduleName) => moduleRegistry.ContainsKey(moduleName);
        public IEnumerable<string> GetModuleNames() => moduleRegistry.Keys;

        // 递归加载依赖模块
        // 解析脚本中的 import 语句，从 directoryPath 目录查找 .fsm 文件并加载
        // 使用 HashSet 检测循环引用
        internal void LoadModuleWithDependencies(string moduleName, string directoryPath, HashSet<string> loadingStack)
        {
            if (!loadingStack.Add(moduleName))
                throw new ScriptException($"Circular module dependency detected: {moduleName}");
            if (!moduleRegistry.TryGetValue(moduleName, out var moduleInfo))
            {
                // 尝试从文件加载
                var filePath = Path.Combine(directoryPath ?? ".", moduleName + ".fsm");
                if (!File.Exists(filePath))
                    throw new ScriptException($"Module file not found: {filePath}");
                RegisterModuleByFile(moduleName, filePath);
                moduleInfo = moduleRegistry[moduleName];
            }
            // 解析 import 并递归
            var imports = ParseImportStatements(moduleInfo.ScriptContent);
            foreach (var imp in imports)
            {
                var depDir = moduleInfo.DirectoryPath ?? directoryPath;
                LoadModuleWithDependencies(imp, depDir ?? ".", loadingStack);
            }
            // 递归加载完依赖后，解析模块自身的内容
            if (moduleInfo.TemplateNodes.Count == 0 && !string.IsNullOrEmpty(moduleInfo.ScriptContent))
            {
                ParseModuleContent(moduleInfo);
            }
        }

        // 解析已加载模块的脚本内容，填充 TemplateNodes/TemplateEvents/PendingTransitions
        // 使用临时字典，防止 module 块外的内容泄露到引擎
        internal void ParseModuleContent(FsmModuleInfo moduleInfo)
        {
            var tempNodeDict = new Dictionary<string, IFSMNode>();
            var tempEventDict = new Dictionary<string, FSMEvent>();

            var stream = new AntlrInputStream(moduleInfo.ScriptContent);
            var lexer = new StateMachineScriptLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new StateMachineScriptParser(tokens);
            var tree = parser.machine();

            var visitor = new BuildStateVisitor(tempEventDict, tempNodeDict, nodeFactory, this);
            visitor.Visit(tree);

            // 回写：如果 VisitModule_statement 没有替换 registry 条目（脚本无 module 块），
            // 将临时字典内容写入传入的 moduleInfo
            if (moduleRegistry.TryGetValue(moduleInfo.ModuleName, out var registered) && ReferenceEquals(registered, moduleInfo))
            {
                moduleInfo.TemplateNodes = tempNodeDict;
                moduleInfo.TemplateEvents = tempEventDict;
            }
        }

        // 简易 import 解析，跳过注释和字符串字面量
        private static List<string> ParseImportStatements(string script)
        {
            // 移除块注释 /* ... */
            var cleaned = Regex.Replace(script, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            // 移除行注释 // ...
            cleaned = Regex.Replace(cleaned, @"//[^\r\n]*", " ");
            // 移除字符串字面量 " ... "
            cleaned = Regex.Replace(cleaned, @"""[^""]*""", "\"\"");
            var result = new List<string>();
            var matches = Regex.Matches(cleaned, @"import\s+([_\w][_\w.]*)");
            foreach (Match match in matches)
                result.Add(match.Groups[1].Value);
            return result;
        }
    }
}
