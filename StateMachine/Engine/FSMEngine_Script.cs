using Antlr4.Runtime;
using System.Diagnostics;
using System.Reflection;

namespace StateMachine
{
    internal static class BaseGroupNodeHelper
    {
        public static bool TrySubscribeEvent<T>(object target, string eventName, T subscriber, string methodName)
        {
            if (target == null) return false;

            var eventInfo = target.GetType().GetBaseGroupNodeType().GetEvent(eventName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (eventInfo != null)
            {
                MethodInfo methodInfo = typeof(T).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
                Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, subscriber, methodInfo);

                MethodInfo addMethod = eventInfo.GetAddMethod(true);
                addMethod.Invoke(target, [handler]);
                return true;
            }
            return false;
        }

        public static bool TryUnsubscribeEvent<T>(object target, string eventName, T subscriber, string methodName)
        {
            if (target == null) return false;

            var eventInfo = target.GetType().GetBaseGroupNodeType().GetEvent(eventName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (eventInfo != null)
            {
                MethodInfo methodInfo = typeof(T).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
                Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, subscriber, methodInfo);

                MethodInfo removeMethod = eventInfo.GetRemoveMethod(true);
                removeMethod.Invoke(target, [handler]);
                return true;
            }
            return false;
        }

        // 方法3：获取继承链中所有的 ABC<> 基类
        public static Type GetBaseGroupNodeType(this Type type)
        {
            return type.GetBaseTypes()
                       .First(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BaseGroupNode<>));
        }

        // 获取类型的所有基类（包括间接基类）
        private static IEnumerable<Type> GetBaseTypes(this Type type)
        {
            var current = type.BaseType;
            while (current != null && current != typeof(object))
            {
                yield return current;
                current = current.BaseType;
            }
        }

        public static bool InheritsFromBaseGroupNode(this Type type)
        {
            if (type == null) return false;

            var current = type.BaseType;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType &&
                    current.GetGenericTypeDefinition() == typeof(BaseGroupNode<>))
                {
                    return true;
                }
                current = current.BaseType;
            }
            return false;
        }
    }
    //通过脚本创建流程结构
    public partial class FSMEngine
    {
        public void CreateStateMachine(string input)
        {
            UnhandleGroupNode();

            ClearEvents();
            ClearNodes();
            var stream = new AntlrInputStream(input);
            var lexer = new StateMachineScriptLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new StateMachineScriptParser(tokens);
            var tree = parser.machine();

            var state = new BuildStateVisitor(eventDict, nodeDict, nodeFactory);
            state.Visit(tree);
            var transition = new BuildTransitionVisitor(eventDict, nodeDict);
            transition.Visit(tree);

            HandleGroupNode();
        }

        public void ReinitGroupNode()
        {
            UnhandleGroupNode();
            HandleGroupNode();
        }

        internal void UnhandleGroupNode()
        {
            foreach (var node in nodeDict)
            {
                if (node.Value is BaseGroupNode gn)
                {
                    gn.NodeStateChanged -= Gn_NodeStateChanged;
                    gn.NodeExitChanged -= Gn_NodeExitChanged;
                }
                else if (node.Value.GetType().InheritsFromBaseGroupNode())
                {
                    BaseGroupNodeHelper.TryUnsubscribeEvent(node.Value, "NodeStateChanged", this, "Gn_NodeStateChanged");
                    BaseGroupNodeHelper.TryUnsubscribeEvent(node.Value, "NodeExitChanged", this, "Gn_NodeExitChanged");
                }
            }
        }

        internal void HandleGroupNode()
        {
            foreach (var node in nodeDict)
            {
                node.Value.SetEngine(this);
                if (node.Value is BaseGroupNode gn)
                {
                    gn.NodeStateChanged += Gn_NodeStateChanged;
                    gn.NodeExitChanged += Gn_NodeExitChanged;
                }
                else if (node.Value.GetType().InheritsFromBaseGroupNode())
                {
                    BaseGroupNodeHelper.TrySubscribeEvent(node.Value, "NodeStateChanged", this, "Gn_NodeStateChanged");
                    BaseGroupNodeHelper.TrySubscribeEvent(node.Value, "NodeExitChanged", this, "Gn_NodeExitChanged");
                }
            }
        }

        [DebuggerStepThrough]
        private void Gn_NodeExitChanged(object sender, string e)
        {
            GroupNodeExitChanged?.Invoke(sender, e);
        }

        [DebuggerStepThrough]
        private void Gn_NodeStateChanged(object sender, string e)
        {
            GroupNodeStateChanged?.Invoke(sender, e);
        }

        public event EventHandler<string>? GroupNodeStateChanged;
        public event EventHandler<string>? GroupNodeExitChanged;

        public bool TryCreateStateMachine(string input)
        {
            try
            {
                CreateStateMachine(input);

                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }

        public void CreateStateMachineByFile(string path)
        {
            using (FileStream f = new FileStream(path, FileMode.Open))
            using (StreamReader reader = new StreamReader(f))
                CreateStateMachine(reader.ReadToEnd());
        }

        public bool TryCreateStateMachineByFile(string path)
        {
            try
            {
                CreateStateMachineByFile(path);
                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }

        public void Transform(string input)
        {
            UnhandleGroupNode();

            var stream = new AntlrInputStream(input);
            var lexer = new StateMachineScriptLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new StateMachineScriptParser(tokens);
            var tree = parser.machine();

            foreach (var state in nodeDict.Values)
            {
                state.ClearTransition();
            }

            var transition = new BuildTransitionVisitor(eventDict, nodeDict);
            transition.Visit(tree);

            HandleGroupNode();
        }

        //变形：删除原有连线，重新连线
        public bool TryTransform(string input)
        {
            try
            {
                Transform(input);
                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }

        public void TransformByFile(string path)
        {
            using (FileStream f = new FileStream(path, FileMode.Open))
            using (StreamReader reader = new StreamReader(f))
                Transform(reader.ReadToEnd());
        }

        public bool TryTransformByFile(string path)
        {
            try
            {
                TransformByFile(path);
                return true;
            }
            catch (Exception)
            {
                //throw;
                return false;
            }
        }


        public override string ToString()
        {
            string script = "";
            foreach (var state in this)
            {
                script += state.ToString();
            }
            return script;
        }

    }
}
