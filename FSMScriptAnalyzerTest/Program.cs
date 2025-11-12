using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using StateMachine;

namespace FSMScriptAnalyzerTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== FSMScriptAnalyzer 生成类测试 ===");

            try
            {
                var factory = new ReflectionNodeFactory(
                    new[]
                    {
                        Assembly.Load("DemoNodes"),
                        Assembly.Load("StateMachine"),
                        Assembly.GetExecutingAssembly(),
                    });

                var aTest = ATest.Create(factory);
                var engine = aTest.Engine;
                
                Console.WriteLine("已加载脚本: FSMScriptAnalyzerTest.ATest");
                Console.WriteLine("节点列表:");
                foreach (var name in engine.GetNodeNames())
                {
                    Console.WriteLine($" - {name}");
                }

                Console.WriteLine("事件列表:");
                foreach (var evt in engine.GetEventNames())
                {
                    Console.WriteLine($" - {evt}");
                }

                Console.WriteLine("脚本文本预览（前200字符）:");
                var text = FSMScriptAnalyzerTest.ATest.ScriptText;
                Console.WriteLine(text.Length > 200 ? text.Substring(0, 200) + "..." : text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            Console.WriteLine("=== 完成 ===");
        }
    }

    internal sealed class ReflectionNodeFactory : IFSMNodeFactory
    {
        private readonly Dictionary<string, Type> keyToType;
        private readonly Dictionary<Type, string> typeToKey;

        public ReflectionNodeFactory(IEnumerable<Assembly> assemblies)
        {
            keyToType = new();
            typeToKey = new();

            foreach (var asm in assemblies)
            {
                foreach (var t in asm.GetTypes().Where(t => typeof(IFSMNode).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    var attr = t.GetCustomAttributes(typeof(FSMNodeAttribute), false).FirstOrDefault() as FSMNodeAttribute;
                    if (attr == null || string.IsNullOrWhiteSpace(attr.Key)) continue;
                    if (!keyToType.ContainsKey(attr.Key))
                    {
                        keyToType[attr.Key] = t;
                        typeToKey[t] = attr.Key;
                    }
                }
            }
        }

        public IFSMNode CreateNode(string name)
        {
            if (!keyToType.TryGetValue(name, out var t))
                throw new InvalidOperationException($"Node key '{name}' 未注册");
            return (IFSMNode)Activator.CreateInstance(t)!;
        }

        public Type GetNodeType(string name)
        {
            if (!keyToType.TryGetValue(name, out var t))
                throw new InvalidOperationException($"Node key '{name}' 未注册");
            return t;
        }

        public string GetNodeName(Type type)
        {
            if (typeToKey.TryGetValue(type, out var key)) return key;
            var attr = type.GetCustomAttributes(typeof(FSMNodeAttribute), false).FirstOrDefault() as FSMNodeAttribute;
            if (attr != null && !string.IsNullOrWhiteSpace(attr.Key)) return attr.Key;
            throw new InvalidOperationException($"类型 {type.FullName} 未标记 FSMNodeAttribute");
        }

        public IEnumerable<Type> GetNodeTypes() => keyToType.Values;
    }
}
