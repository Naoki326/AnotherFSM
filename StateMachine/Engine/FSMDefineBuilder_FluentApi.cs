namespace StateMachine
{
    public interface IFSMDefineBuilder
    {

        IFSMDefineBuilder AddNode(string nodeName, string nodeType);
        IFSMDefineBuilder AddNode(string nodeName, string nodeType, Action<IFSMNodeDefineBuilder> definer);
        IFSMDefineBuilder AddNode(Enum nodeName, string nodeType);
        IFSMDefineBuilder AddNode(Enum nodeName, string nodeType, Action<IFSMNodeDefineBuilder> definer);

        IFSMDefineBuilder AddNode<T>(string nodeName) where T : IFSMNode;
        IFSMDefineBuilder AddNode<T>(string nodeName, Action<IFSMNodeDefineBuilder> definer) where T : IFSMNode;
        IFSMDefineBuilder AddNode<T>(string nodeName, Action<IFSMNodeDefineBuilder> definer, Action<T> afterRun) where T : IFSMNode;
        IFSMDefineBuilder AddNode<T>(Enum nodeName) where T : IFSMNode;
        IFSMDefineBuilder AddNode<T>(Enum nodeName, Action<IFSMNodeDefineBuilder> definer) where T : IFSMNode;
        IFSMDefineBuilder AddNode<T>(Enum nodeName, Action<IFSMNodeDefineBuilder> definer, Action<T> afterRun) where T : IFSMNode;

        IFSMDefineBuilder AddConnection(string connectionName, string fromNode, string toNode);
        IFSMDefineBuilder AddConnection(Enum connectionName, Enum fromNode, Enum toNode);

        void Build();
    }

    public interface IFSMNodeDefineBuilder
    {
        IFSMNodeDefineBuilder SetEventBinding(FSMEnum eventEnum, Enum eventName);
        IFSMNodeDefineBuilder SetEventBinding(FSMEnum eventEnum, string eventName);
        IFSMNodeDefineBuilder SetEventBinding(int eventIndex, Enum eventName);
        IFSMNodeDefineBuilder SetEventBinding(int eventIndex, string eventName);
    }

    internal class FSMDefineBuilder : IFSMDefineBuilder
    {
        private FSMEngine engine;
        private FSMDefineBuilder(FSMEngine engine) { this.engine = engine; }
        public static IFSMDefineBuilder Create(FSMEngine engine) => new FSMDefineBuilder(engine);

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

        public IFSMDefineBuilder AddNode(string nodeName, string nodeType, Action<IFSMNodeDefineBuilder> definer)
        {
            AddNode(nodeName, nodeType);
            definer?.Invoke(new FSMNodeDefineBuilder(engine, engine[(ScriptNode)nodeName]));
            return this;
        }

        public IFSMDefineBuilder AddNode(Enum nodeName, string nodeType, Action<IFSMNodeDefineBuilder> definer)
        {
            AddNode(nodeName, nodeType);
            definer?.Invoke(new FSMNodeDefineBuilder(engine, engine[(ScriptNode)nodeName]));
            return this;
        }

        public IFSMDefineBuilder AddNode<T>(string nodeName, Action<IFSMNodeDefineBuilder> definer) where T : IFSMNode
        {
            AddNode<T>(nodeName);
            definer?.Invoke(new FSMNodeDefineBuilder(engine, engine[(ScriptNode)nodeName]));
            return this;
        }

        public IFSMDefineBuilder AddNode<T>(string nodeName, Action<IFSMNodeDefineBuilder> definer, Action<T> continueWith) where T : IFSMNode
        {
            AddNode<T>(nodeName);
            definer?.Invoke(new FSMNodeDefineBuilder(engine, engine[(ScriptNode)nodeName]));
            T node = (T)engine[(ScriptNode)nodeName];
            node.ContinueWith += n => { continueWith((T)n); };
            return this;
        }

        public IFSMDefineBuilder AddNode<T>(Enum nodeName, Action<IFSMNodeDefineBuilder> definer) where T : IFSMNode
        {
            AddNode<T>(nodeName);
            definer?.Invoke(new FSMNodeDefineBuilder(engine, engine[(ScriptNode)nodeName]));
            return this;
        }

        public void A<T>(Action<T> afterRun) where T : IFSMNode
        {
            Action<IFSMNode> x = (node) => { };
            afterRun = (T node) => { x(node); };
        }

        public IFSMDefineBuilder AddNode<T>(Enum nodeName, Action<IFSMNodeDefineBuilder> definer, Action<T> continueWith) where T : IFSMNode
        {
            AddNode<T>(nodeName);
            definer?.Invoke(new FSMNodeDefineBuilder(engine, engine[(ScriptNode)nodeName]));
            T node = (T)engine[(ScriptNode)nodeName];
            node.ContinueWith += n => { continueWith((T)n); };
            return this;
        }

        private List<(string connectionName, string fromNode, string toNode)> connections = [];

        public IFSMDefineBuilder AddConnection(string connectionName, string fromNode, string toNode)
        {
            connections.Add((connectionName, fromNode, toNode));
            return this;
        }

        public IFSMDefineBuilder AddConnection(Enum connectionName, Enum fromNode, Enum toNode)
        {
            connections.Add(
                (Enum.GetName(connectionName.GetType(), connectionName),
                    Enum.GetName(fromNode.GetType(), fromNode),
                    Enum.GetName(toNode.GetType(), toNode))
                );
            return this;
        }

        public void Build()
        {
            foreach (var (connectionName, fromNode, toNode) in connections)
            {
                engine.ConnectNode(connectionName, fromNode, toNode);
            }
        }
    }

    public class FSMNodeDefineBuilder(FSMEngine engine, IFSMNode node) : IFSMNodeDefineBuilder
    {

        public IFSMNodeDefineBuilder SetEventBinding(FSMEnum eventEnum, Enum eventName)
        {
            string name = Enum.GetName(eventName.GetType(), eventName);
            if (!engine.TryGetEvent(name, out _))
            {
                engine.AddEvent(new FSMEvent(name));
            }
            node.SetBranchEvent((int)eventEnum, engine[(ScriptEvent)eventName]);
            return this;
        }

        public IFSMNodeDefineBuilder SetEventBinding(FSMEnum eventEnum, string eventName)
        {
            string name = Enum.GetName(eventName.GetType(), eventName);
            if (!engine.TryGetEvent(name, out _))
            {
                engine.AddEvent(new FSMEvent(name));
            }
            node.SetBranchEvent((int)eventEnum, engine[(ScriptEvent)eventName]);
            return this;
        }

        public IFSMNodeDefineBuilder SetEventBinding(int eventIndex, Enum eventName)
        {
            string name = Enum.GetName(eventName.GetType(), eventName);
            if (!engine.TryGetEvent(name, out _))
            {
                engine.AddEvent(new FSMEvent(name));
            }
            node.SetBranchEvent(eventIndex, engine[(ScriptEvent)eventName]);
            return this;
        }

        public IFSMNodeDefineBuilder SetEventBinding(int eventIndex, string eventName)
        {
            string name = Enum.GetName(eventName.GetType(), eventName);
            if (!engine.TryGetEvent(name, out _))
            {
                engine.AddEvent(new FSMEvent(name));
            }
            node.SetBranchEvent(eventIndex, engine[(ScriptEvent)eventName]);
            return this;
        }
    }

    public interface IFSMBuilder
    {
        IFSMBuilderStepConstruct ConfigureNodeFactory(IFSMNodeFactory nodeFactory);
    }

    public interface IFSMBuilderStepConstruct
    {
        IFSMBuilderStepConstruct ConfigureScript(string script);
        IFSMBuilderStepConstruct ConfigureScriptFile(string fileName);
        IFSMBuilderStepConstruct ConfigureFSMDefine(Action<IFSMDefineBuilder> definer);

        //不使用Fluent api来构建状态机，而是使用其他api来构建
        FSMEngine Build();
    }

    public class FSMEngineBuilder : IFSMBuilder, IFSMBuilderStepConstruct
    {
        protected FSMEngine? engine;

        public static IFSMBuilder Create() => new FSMEngineBuilder();

        public static IFSMBuilderStepConstruct Create(FSMEngine e) => new FSMEngineBuilder() { engine = e };

        public static IFSMBuilderStepConstruct Create(IFSMNodeFactory f) => new FSMEngineBuilder().ConfigureNodeFactory(f);

        public IFSMBuilderStepConstruct ConfigureScript(string script)
        {
            engine.Transform(script);
            return this;
        }

        public IFSMBuilderStepConstruct ConfigureScriptFile(string fileName)
        {
            engine.TransformByFile(fileName);
            return this;
        }

        public FSMEngine Build()
        {
            return engine;
        }

        public IFSMBuilderStepConstruct ConfigureNodeFactory(IFSMNodeFactory nodeFactory)
        {
            engine = new FSMEngine(nodeFactory);
            return this;
        }

        public IFSMBuilderStepConstruct ConfigureFSMDefine(Action<IFSMDefineBuilder> definer)
        {
            var builder = FSMDefineBuilder.Create(engine);
            definer(builder);
            return this;
        }
    }
}
