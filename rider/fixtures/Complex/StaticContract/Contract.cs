// STATIC SYMBOL FIXTURE ONLY. Not the real AnotherFSM runtime or a compatible substitute.
using System;
using System.IO;

namespace StateMachine
{
    public interface IFSMNode { void Execute(); }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class FSMNodeAttribute : Attribute
    {
        public FSMNodeAttribute(string key) => FixtureSafety.Execution.Forbidden<object>("FSMNodeAttribute.ctor");
        public FSMNodeAttribute(string key, int[] indexes, string[] events) =>
            FixtureSafety.Execution.Forbidden<object>("FSMNodeAttribute.ctor(indexes)");
        public string Key
        {
            get => FixtureSafety.Execution.Forbidden<string>("FSMNodeAttribute.Key.get");
            set => FixtureSafety.Execution.Forbidden<object>("FSMNodeAttribute.Key.set");
        }
        public int[]? Indexes;
        public string[]? EventDescriptions;
    }
}

namespace FixtureSafety
{
    public static class Execution
    {
        public const string MarkerVariable = "ANOTHERFSM_COMPLEX_EXECUTION_MARKER";
        public static T Forbidden<T>(string member)
        {
            var path = Environment.GetEnvironmentVariable(MarkerVariable);
            // Only an explicitly supplied file destination is written. Never infer a cwd/credential path.
            if (!string.IsNullOrEmpty(path))
            {
                if (!Path.IsPathRooted(path))
                    throw new InvalidOperationException("ISSUE34_BUSINESS_EXECUTION: marker path must be absolute");
                File.AppendAllText(path, DateTime.UtcNow.ToString("O") + "\t" + member + Environment.NewLine);
            }
            throw new InvalidOperationException("ISSUE34_BUSINESS_EXECUTION: " + member);
        }
    }

    public abstract class ControlledNode : StateMachine.IFSMNode
    {
        protected ControlledNode() => Execution.Forbidden<object>("ControlledNode.ctor");
        public virtual void Execute() => Execution.Forbidden<object>("ControlledNode.Execute");
        public string BusinessValue => Execution.Forbidden<string>("ControlledNode.BusinessValue.get");
    }

    // This is intentionally not an editor registration convention.
    public sealed class BusinessFactory
    {
        public BusinessFactory() => Execution.Forbidden<object>("BusinessFactory.ctor");
        public static BusinessFactory Current => Execution.Forbidden<BusinessFactory>("BusinessFactory.Current.get");
        public StateMachine.IFSMNode CreateNode(string key) => Execution.Forbidden<StateMachine.IFSMNode>("BusinessFactory.CreateNode");
        public Type GetNodeType(string key) => Execution.Forbidden<Type>("BusinessFactory.GetNodeType");
    }
    public static class BusinessEntry
    {
        public static void Run() => Execution.Forbidden<object>("BusinessEntry.Run");
    }
}
