using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Antlr4.Runtime.Atn;

namespace StateMachine
{

    public interface IFSMNodeFactory
    {
        // 创建节点
        IFSMNode CreateNode(string name);

        // 获取该key对应的节点类型
        Type GetNodeType(string name);

        // 获取所有可用的节点类型
        IEnumerable<Type> GetNodeTypes();
    }

    internal class AssembleNodeFactory : IFSMNodeFactory
    {

        private List<Assembly> assemblies = [];
        public void AddAssemble(Assembly assembly)
        {
            assemblies.Add(assembly);
        }

        public IFSMNode CreateNode(string name)
        {
            Type targetType = assemblies.SelectMany(p => p.GetTypes())
                .FirstOrDefault(type =>
                {
                    var attribute = type.GetCustomAttribute<FSMNodeAttribute>();
                    return attribute != null && attribute.Key == name;
                });
            if (targetType != null)
            {
                // 创建实例
                return (IFSMNode)Activator.CreateInstance(targetType);
            }
            else
            {
                throw new ScriptException("State " + name + " 定义出错, " + "未找到该State！");
            }
        }

        public IEnumerable<FSMNodeAttribute> GetEnabledNodes()
        {
            return [.. assemblies.SelectMany(ass => ass.GetTypes())
                    .Where(p => p.GetCustomAttributes<FSMNodeAttribute>().Any())
                    .OrderBy(p => p.GetCustomAttributes<FSMNodeAttribute>().First().Id)
                    .Select(p => p.GetCustomAttributes<FSMNodeAttribute>().First())
            ];
        }

        public FSMNodeAttribute GetNodeAttribute(string name)
        {
            FSMNodeAttribute targetAttribute = assemblies.SelectMany(p => p.GetTypes())
                .FirstOrDefault(type =>
                {
                    var attribute = type.GetCustomAttribute<FSMNodeAttribute>();
                    return attribute != null && attribute.Key == name;
                })
                .GetCustomAttribute<FSMNodeAttribute>();
            if (targetAttribute == null)
            {
                throw new ScriptException("State " + name + " 定义出错, " + "未找到该State！");
            }
            return targetAttribute;
        }

        public Type GetNodeType(string name)
        {
            Type targetType = assemblies.SelectMany(p => p.GetTypes())
                .FirstOrDefault(type =>
                {
                    var attribute = type.GetCustomAttribute<FSMNodeAttribute>();
                    return attribute != null && attribute.Key == name;
                });
            if (targetType is null)
            {
                throw new ScriptException("State " + name + " 定义出错, " + "未找到该State！");
            }
            return targetType;
        }

        public IEnumerable<Type> GetNodeTypes()
        {
            return [..assemblies.SelectMany(ass => ass.GetTypes())];
        }
    }
}
