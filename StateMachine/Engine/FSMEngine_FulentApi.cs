using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace StateMachine
{
    public interface IFSMDefineBuilder
    {

        IFSMDefineBuilder AddNode(string nodeName, string nodeType);
        IFSMDefineBuilder AddNode(Enum nodeName, string nodeType);

        IFSMDefineBuilder AddNode<T>(string nodeName) where T : IFSMNode;
        IFSMDefineBuilder AddNode<T>(Enum nodeName) where T : IFSMNode;

        IFSMDefineBuilder AddConnection(string connectionName, string fromNode, string toNode);
        IFSMDefineBuilder AddConnection(Enum connectionName, Enum fromNode, Enum toNode);
    }

    internal class FSMDefineBuilder : IFSMDefineBuilder
    {
        private FSMEngine engine;
        private FSMDefineBuilder(FSMEngine engine) { this.engine = engine; }
        public static IFSMDefineBuilder Create(FSMEngine engine) => new FSMDefineBuilder(engine);
        public IFSMDefineBuilder AddConnection(string connectionName, string fromNode, string toNode)
        {
            engine.ConnectNode(connectionName, fromNode, toNode);
            return this;
        }
        public IFSMDefineBuilder AddConnection(Enum connectionName, Enum fromNode, Enum toNode)
        {
            engine.ConnectNode(connectionName.ToString(), fromNode.ToString(), toNode.ToString());
            return this;
        }

        public IFSMDefineBuilder AddNode(string nodeName, string nodeType)
        {
            engine.CreateNode(nodeType, nodeName);
            return this;
        }

        public IFSMDefineBuilder AddNode(Enum nodeName, string nodeType)
        {
            engine.CreateNode(nodeType, nodeName.ToString());
            return this;
        }

        public IFSMDefineBuilder AddNode<T>(string nodeName) where T : IFSMNode
        {
            engine.CreateNode<T>(nodeName);
            return this;
        }

        public IFSMDefineBuilder AddNode<T>(Enum nodeName) where T : IFSMNode
        {
            engine.CreateNode<T>(nodeName.ToString());
            return this;
        }
    }

    public interface IFSMAssemblesBuilder
    {
        IFSMAssemblesBuilder AddAssemble(Assembly assembly);
        IFSMAssemblesBuilder AddAssembles(IEnumerable<Assembly> assemblies);
        IFSMAssemblesBuilder AddAssemblePath(string path);

        IEnumerable<Assembly> Build();
    }
    internal class FSMAssemblesBuilder : IFSMAssemblesBuilder
    {
        private IEnumerable<Assembly> fsmAssemblies = Enumerable.Empty<Assembly>();

        public static IFSMAssemblesBuilder Create() => new FSMAssemblesBuilder();

        public IEnumerable<Assembly> Build()
        {
            return fsmAssemblies;
        }

        public IFSMAssemblesBuilder AddAssemble(Assembly assembly)
        {
            fsmAssemblies = [.. fsmAssemblies, assembly];
            return this;
        }

        public IFSMAssemblesBuilder AddAssemblePath(string path)
        {
            Assembly assembly = Assembly.LoadFrom(path);
            fsmAssemblies = [.. fsmAssemblies, assembly];
            return this;
        }

        public IFSMAssemblesBuilder AddAssembles(IEnumerable<Assembly> assemblies)
        {
            fsmAssemblies = [.. fsmAssemblies, ..assemblies];
            return this;
        }
    }

    public interface IFSMBuilder
    {
        IFSMBuilderStepConstruct ConfigureAssembles(Action<IFSMAssemblesBuilder> assembleBuilder);
    }

    public interface IFSMBuilderStepConstruct
    {
        IFSMBuilderStepEnd ConfigureScript(string script);
        IFSMBuilderStepEnd ConfigureScriptFile(string fileName);
        IFSMBuilderStepEnd ConfigureFSMDefine(Action<IFSMDefineBuilder> definer);

        //不使用Fluent api来构建状态机，而是使用其他api来构建
        FSMEngine Build();
    }

    public interface IFSMBuilderStepEnd
    {
        FSMEngine Build();
    }

    public class FSMEngineBuilder : IFSMBuilder, IFSMBuilderStepEnd, IFSMBuilderStepConstruct
    {
        protected FSMEngine engine;

        private FSMEngineBuilder()
        {
            engine = new FSMEngine();
        }

        private FSMEngineBuilder(FSMEngine e)
        {
            engine = e;
        }

        public static IFSMBuilder Create() => new FSMEngineBuilder();

        public static IFSMBuilder Create(FSMEngine e) => new FSMEngineBuilder(e);

        public IFSMBuilderStepEnd ConfigureScript(string script)
        {
            engine.CreateStateMachine(script);
            return this;
        }

        public IFSMBuilderStepEnd ConfigureScriptFile(string fileName)
        {
            engine.CreateStateMachineByFile(fileName);
            return this;
        }

        public FSMEngine Build()
        {
            return engine;
        }

        public IFSMBuilderStepConstruct ConfigureAssembles(Action<IFSMAssemblesBuilder> assembleBuilder)
        {
            var assBuilder = FSMAssemblesBuilder.Create();
            assembleBuilder(assBuilder);
            foreach(var ass in assBuilder.Build())
            {
                engine.AddAssemblyForNode(ass);
            }
            return this;
        }

        public IFSMBuilderStepEnd ConfigureFSMDefine(Action<IFSMDefineBuilder> definer)
        {
            var builder = FSMDefineBuilder.Create(engine);
            definer(builder);
            return this;
        }
    }
}
