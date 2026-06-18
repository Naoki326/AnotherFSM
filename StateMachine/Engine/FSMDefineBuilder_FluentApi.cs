namespace StateMachine
{

    public interface IFSMTransformBuilder
    {
        IFSMTransformBuilder AddTransform(string connectionName, string fromNode, string toNode);
        IFSMTransformBuilder AddTransform(Enum connectionName, Enum fromNode, Enum toNode);
        IFSMTransformBuilder AddTransform(FSMEvent connection, IFSMNode fromNode, IFSMNode toNode);

        void Build();
    }

    internal class FSMTransformBuilder : IFSMTransformBuilder
    {
        private FSMEngine engine;
        private FSMTransformBuilder(FSMEngine engine) { this.engine = engine; }
        public static IFSMTransformBuilder Create(FSMEngine engine) => new FSMTransformBuilder(engine);

        private List<(string connectionName, string fromNode, string toNode)> connections = [];

        private List<string> ResolveSourceNodes(string connectionName, string fromNode)
        {
            if (!engine.moduleInstances.TryGetValue(fromNode, out var instance))
                return [fromNode];

            if (instance.ExternalEventToInternalNodes.TryGetValue(connectionName, out var outputNodes)
                && outputNodes.Count > 0)
                return outputNodes;

            if (instance.TerminalNodeNames.Count > 0)
            {
                var matchingTerminals = instance.TerminalNodeNames
                    .Where(nodeName => engine.TryGetNode(nodeName, out var node)
                        && FSMNodeBranchEventHelper.PublishesEvent(node, connectionName))
                    .ToList();

                if (matchingTerminals.Count > 0)
                    return matchingTerminals;

                throw new ScriptException($"模块实例 {fromNode} 连线出错, 没有 terminal 节点发布事件 {connectionName}！");
            }

            throw new ScriptException($"模块实例 {fromNode} 连线出错, 未声明 terminal 节点！");
        }

        private List<string> ResolveTargetNodes(string toNode)
        {
            if (!engine.moduleInstances.TryGetValue(toNode, out var instance))
                return [toNode];

            if (instance.InputNodeNames.Count > 0)
                return instance.InputNodeNames;

            throw new ScriptException($"模块实例 {toNode} 连线出错, 未声明 input 节点！");
        }

        private void EnsureTerminalSourcePublishesEvent(string connectionName, string fromNode, string sourceNode)
        {
            if (!engine.moduleInstances.TryGetValue(fromNode, out var instance)
                || !instance.TerminalNodeNames.Any(nodeName => string.Equals(nodeName, sourceNode, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (!engine.TryGetEvent(connectionName, out var fsmEvent))
            {
                fsmEvent = new FSMEvent(connectionName);
                engine.AddEvent(fsmEvent);
            }

            if (!FSMNodeBranchEventHelper.EnsurePublishesEvent(engine[sourceNode], fsmEvent))
                throw new ScriptException($"模块实例 {fromNode} 的 terminal 节点 {sourceNode} 有多个分支，无法自动匹配外部事件 {connectionName}！");
        }

        public IFSMTransformBuilder AddTransform(string connectionName, string fromNode, string toNode)
        {
            connections.Add((connectionName, fromNode, toNode));
            return this;
        }

        public IFSMTransformBuilder AddTransform(Enum connectionName, Enum fromNode, Enum toNode)
        {
            connections.Add(
                (Enum.GetName(connectionName.GetType(), connectionName),
                    Enum.GetName(fromNode.GetType(), fromNode),
                    Enum.GetName(toNode.GetType(), toNode))
                );
            return this;
        }

        public IFSMTransformBuilder AddTransform(FSMEvent connection, IFSMNode fromNode, IFSMNode toNode)
        {
            connections.Add((connection.EventName, fromNode.Name, toNode.Name));
            return this;
        }

        public void Build()
        {
            foreach (var connection in connections)
            {
                foreach (var sourceNode in ResolveSourceNodes(connection.connectionName, connection.fromNode))
                    foreach (var targetNode in ResolveTargetNodes(connection.toNode))
                    {
                        EnsureTerminalSourcePublishesEvent(connection.connectionName, connection.fromNode, sourceNode);
                        engine.TryForceConnectNode(connection.connectionName, sourceNode, targetNode);
                    }
            }
            engine.HandleGroupNode();
        }
    }


    public interface IFSMModuleConfigurator
    {
        IFSMModuleConfigurator MapOutput(string outputEventName, string externalEventName);
        IFSMModuleConfigurator SetPosition(double x, double y);
        IFSMModuleConfigurator SetColor(string color);
        IFSMModuleConfigurator SetFlowID(string flowId);
    }

    internal class FSMModuleConfigurator(FsmModuleInstance instance) : IFSMModuleConfigurator
    {
        public IFSMModuleConfigurator MapOutput(string outputEventName, string externalEventName)
        {
            instance.OutputEventMap[outputEventName] = externalEventName;
            return this;
        }

        public IFSMModuleConfigurator SetPosition(double x, double y)
        {
            instance.PosX = x;
            instance.PosY = y;
            return this;
        }

        public IFSMModuleConfigurator SetColor(string color)
        {
            instance.Color = color;
            return this;
        }

        public IFSMModuleConfigurator SetFlowID(string flowId)
        {
            instance.FlowID = flowId;
            return this;
        }
    }

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
        IFSMDefineBuilder AddConnection(FSMEvent connection, IFSMNode fromNode, IFSMNode toNode);

        IFSMDefineBuilder RegisterModule(string moduleName, string scriptContent, string? directory = null);
        IFSMDefineBuilder RegisterModuleFile(string moduleName, string filePath);
        IFSMDefineBuilder AddModule(string moduleName, string instanceName, Action<IFSMModuleConfigurator>? configure = null);

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

        private List<FsmModuleInstance> modulePendingExpansion = [];

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

        public IFSMDefineBuilder AddNode<T>(Enum nodeName, Action<IFSMNodeDefineBuilder> definer, Action<T> continueWith) where T : IFSMNode
        {
            AddNode<T>(nodeName);
            definer?.Invoke(new FSMNodeDefineBuilder(engine, engine[(ScriptNode)nodeName]));
            T node = (T)engine[(ScriptNode)nodeName];
            node.ContinueWith += n => { continueWith((T)n); };
            return this;
        }

        private List<(string connectionName, string fromNode, string toNode)> connections = [];

        private List<string> ResolveSourceNodes(string connectionName, string fromNode)
        {
            if (!engine.moduleInstances.TryGetValue(fromNode, out var instance))
                return [fromNode];

            if (instance.ExternalEventToInternalNodes.TryGetValue(connectionName, out var outputNodes)
                && outputNodes.Count > 0)
                return outputNodes;

            if (instance.TerminalNodeNames.Count > 0)
            {
                var matchingTerminals = instance.TerminalNodeNames
                    .Where(nodeName => engine.TryGetNode(nodeName, out var node)
                        && FSMNodeBranchEventHelper.PublishesEvent(node, connectionName))
                    .ToList();

                if (matchingTerminals.Count > 0)
                    return matchingTerminals;

                throw new ScriptException($"模块实例 {fromNode} 连线出错, 没有 terminal 节点发布事件 {connectionName}！");
            }

            throw new ScriptException($"模块实例 {fromNode} 连线出错, 未声明 terminal 节点！");
        }

        private List<string> ResolveTargetNodes(string toNode)
        {
            if (!engine.moduleInstances.TryGetValue(toNode, out var instance))
                return [toNode];

            if (instance.InputNodeNames.Count > 0)
                return instance.InputNodeNames;

            throw new ScriptException($"模块实例 {toNode} 连线出错, 未声明 input 节点！");
        }

        private void EnsureTerminalSourcePublishesEvent(string connectionName, string fromNode, string sourceNode)
        {
            if (!engine.moduleInstances.TryGetValue(fromNode, out var instance)
                || !instance.TerminalNodeNames.Any(nodeName => string.Equals(nodeName, sourceNode, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (!engine.TryGetEvent(connectionName, out var fsmEvent))
            {
                fsmEvent = new FSMEvent(connectionName);
                engine.AddEvent(fsmEvent);
            }

            if (!FSMNodeBranchEventHelper.EnsurePublishesEvent(engine[sourceNode], fsmEvent))
                throw new ScriptException($"模块实例 {fromNode} 的 terminal 节点 {sourceNode} 有多个分支，无法自动匹配外部事件 {connectionName}！");
        }

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

        public IFSMDefineBuilder AddConnection(FSMEvent connection, IFSMNode fromNode, IFSMNode toNode)
        {
            connections.Add((connection.EventName, fromNode.Name, toNode.Name));
            return this;
        }

        public IFSMDefineBuilder RegisterModule(string moduleName, string scriptContent, string? directory = null)
        {
            engine.RegisterModule(moduleName, scriptContent, directory);
            return this;
        }

        public IFSMDefineBuilder RegisterModuleFile(string moduleName, string filePath)
        {
            engine.RegisterModuleByFile(moduleName, filePath);
            return this;
        }

        public IFSMDefineBuilder AddModule(string moduleName, string instanceName, Action<IFSMModuleConfigurator>? configure = null)
        {
            var instance = new FsmModuleInstance
            {
                ModuleName = moduleName,
                InstanceName = instanceName,
            };
            configure?.Invoke(new FSMModuleConfigurator(instance));
            modulePendingExpansion.Add(instance);
            return this;
        }

        public void Build()
        {
            ExpandModuleInstances();

            foreach (var (connectionName, fromNode, toNode) in connections)
            {
                foreach (var sourceNode in ResolveSourceNodes(connectionName, fromNode))
                    foreach (var targetNode in ResolveTargetNodes(toNode))
                    {
                        EnsureTerminalSourcePublishesEvent(connectionName, fromNode, sourceNode);
                        engine.ConnectNode(connectionName, sourceNode, targetNode);
                    }
            }
            engine.HandleGroupNode();
        }

        private void ExpandModuleInstances()
        {
            if (modulePendingExpansion.Count == 0) return;

            foreach (var instance in modulePendingExpansion)
            {
                var moduleName = instance.ModuleName;
                if (!engine.TryGetModule(moduleName, out var template))
                    throw new ScriptException($"Module {moduleName} not registered");

                // 加载依赖并解析模块内容
                var loadingStack = new HashSet<string>();
                engine.LoadModuleWithDependencies(moduleName, template.DirectoryPath ?? ".", loadingStack);

                // 重新获取（LoadModuleWithDependencies 可能替换了 registry 条目）
                if (!engine.TryGetModule(moduleName, out template))
                    throw new ScriptException($"Module {moduleName} lost from registry during expansion");

                if (engine.moduleInstances.ContainsKey(instance.InstanceName))
                {
                    throw new ScriptException($"模块实例 {instance.InstanceName} 已存在，不能重复定义！");
                }

                string prefix = instance.InstanceName + ".";
                var outputMap = instance.OutputEventMap;

                // 确保模板节点 EventDescriptions 是最新的
                foreach (var kvp in template.TemplateNodes)
                    kvp.Value.UpdateEventDescriptions();

                // Phase 1: 创建带前缀的节点
                instance.InputNodeNames.Clear();
                instance.TerminalNodeNames.Clear();
                foreach (var kvp in template.TemplateNodes)
                {
                    var templateNodeName = kvp.Key;
                    var templateNode = kvp.Value;
                    var newNodeName = prefix + templateNodeName;

                    engine.CreateNode(templateNode.ClassType, newNodeName);
                    var newNode = engine[(ScriptNode)newNodeName];

                    // 从模板复制视觉属性
                    newNode.PosX = templateNode.PosX;
                    newNode.PosY = templateNode.PosY;
                    newNode.Color = templateNode.Color;
                    newNode.FlowID = templateNode.FlowID;

                    // 复制 GroupNode/ParallelNode 信息（带前缀）
                    CopyGroupInfo(templateNode, newNode, prefix);

                    // 应用分支事件（含 output 映射 / 前缀）
                    foreach (var desc in templateNode.EventDescriptions)
                    {
                        string descEventName = desc.Description;
                        int index = desc.Index;

                        string finalEventName;
                        if (outputMap.TryGetValue(descEventName, out string? mappedEvent))
                        {
                            finalEventName = mappedEvent;
                            // 记录发出 output 事件的节点供外部连线使用
                            if (!instance.ExternalEventToInternalNodes.ContainsKey(mappedEvent))
                                instance.ExternalEventToInternalNodes[mappedEvent] = [];
                            if (!instance.ExternalEventToInternalNodes[mappedEvent].Contains(newNodeName))
                                instance.ExternalEventToInternalNodes[mappedEvent].Add(newNodeName);
                        }
                        else
                        {
                            finalEventName = prefix + descEventName;
                        }

                        if (!engine.TryGetEvent(finalEventName, out _))
                            engine.AddEvent(new FSMEvent(finalEventName));

                        newNode.SetBranchEvent(index, engine[(ScriptEvent)finalEventName]);
                    }

                    if (template.Inputs.Contains(templateNodeName, StringComparer.OrdinalIgnoreCase))
                        instance.InputNodeNames.Add(newNodeName);
                    if (template.Terminals.Contains(templateNodeName, StringComparer.OrdinalIgnoreCase))
                        instance.TerminalNodeNames.Add(newNodeName);
                }

                // Phase 2 前：检测 output 映射值是否与某个内部事件展开后的全名碰撞，
                // 碰撞会让两者合并为同一个 FSMEvent，产生跨边界联动。
                var internalEventFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in template.TemplateEvents.Keys)
                {
                    if (!template.Outputs.Contains(name))
                        internalEventFullNames.Add(prefix + name);
                }
                foreach (var mappedPair in outputMap)
                {
                    if (internalEventFullNames.Contains(mappedPair.Value))
                    {
                        throw new ScriptException(
                            $"模块实例 {instance.InstanceName} 的 output 映射出错, 事件 {mappedPair.Value} 与模块内部事件全名碰撞, 请改用不同的外部事件名");
                    }
                }

                // Phase 2: 展开模板事件
                foreach (var kvp in template.TemplateEvents)
                {
                    var eventName = kvp.Key;
                    if (template.Outputs.Contains(eventName) && outputMap.TryGetValue(eventName, out var mappedEvent))
                    {
                        if (!engine.TryGetEvent(mappedEvent, out _))
                            engine.AddEvent(new FSMEvent(mappedEvent));
                    }
                    else
                    {
                        var newEventName = prefix + eventName;
                        if (!engine.TryGetEvent(newEventName, out _))
                            engine.AddEvent(new FSMEvent(newEventName));
                    }
                }

                // Phase 3: 展开模板内部连线
                foreach (var trans in template.PendingTransitions)
                {
                    var sourceNodeName = prefix + trans.SourceNode;
                    var targetNodeName = prefix + trans.TargetNode;

                    string eventName;
                    if (template.Outputs.Contains(trans.EventName) && outputMap.TryGetValue(trans.EventName, out var mappedTransEvent))
                    {
                        eventName = mappedTransEvent;
                    }
                    else
                    {
                        eventName = prefix + trans.EventName;
                    }

                    if (!engine.TryGetEvent(eventName, out var fsmEvent))
                    {
                        fsmEvent = new FSMEvent(eventName);
                        engine.AddEvent(fsmEvent);
                    }

                    if (!engine.TryGetNode(sourceNodeName, out var sourceNode))
                        throw new ScriptException("State " + sourceNodeName + " 连线出错, 未定义该State！");
                    if (!engine.TryGetNode(targetNodeName, out var targetNode))
                        throw new ScriptException("State " + targetNodeName + " 连线出错, 未定义该State！");
                    if (sourceNode.HasTransition(fsmEvent))
                        throw new ScriptException("State " + sourceNodeName + " 向 " + eventName + " 连线出错, 已定义从该状态到该事件连线！");

                    sourceNode.AddTransition(fsmEvent, targetNode);
                }

                // 注册实例
                engine.moduleInstances[instance.InstanceName] = instance;
            }
        }

        private static void CopyGroupInfo(IFSMNode templateNode, IFSMNode newNode, string prefix)
        {
            if (newNode is IGroupNode gn && templateNode is IGroupNode tGn)
            {
                if (!string.IsNullOrEmpty(tGn.StartNode))
                    gn.StartNode = prefix + tGn.StartNode;
                if (!string.IsNullOrEmpty(tGn.EndEvent))
                    gn.EndEvent = prefix + tGn.EndEvent;
            }
            if (newNode is IParallelNode pn && templateNode is IParallelNode tPn)
            {
                foreach (var fsm in tPn.FSMs)
                {
                    pn.FSMs.Add(new FSMDescribe
                    {
                        StartNode = prefix + fsm.StartNode,
                        EndEvent = prefix + fsm.EndEvent,
                    });
                }
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
            if (!engine.TryGetEvent(eventName, out _))
            {
                engine.AddEvent(new FSMEvent(eventName));
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
            if (!engine.TryGetEvent(eventName, out _))
            {
                engine.AddEvent(new FSMEvent(eventName));
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
        IFSMBuilderStepConstruct ConfigureModule(string moduleName, string scriptContent);
        IFSMBuilderStepConstruct ConfigureFSMDefine(Action<IFSMDefineBuilder> definer);
        IFSMBuilderStepConstruct TransformFSMDefine(Action<IFSMTransformBuilder> definer);

        //不使用Fluent api来构建状态机，而是使用其他api来构建
        FSMEngine Build();
    }

    public class FSMEngineBuilder : IFSMBuilder, IFSMBuilderStepConstruct
    {
        protected FSMEngine? engine;

        public static IFSMBuilder Create() => new FSMEngineBuilder();

        public static IFSMBuilderStepConstruct Create(FSMEngine e)
        {
            e.UnhandleGroupNode();
            return new FSMEngineBuilder() { engine = e };
        }

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

        public IFSMBuilderStepConstruct ConfigureModule(string moduleName, string scriptContent)
        {
            engine.RegisterModule(moduleName, scriptContent);
            return this;
        }

        public IFSMBuilderStepConstruct ConfigureFSMDefine(Action<IFSMDefineBuilder> definer)
        {
            var builder = FSMDefineBuilder.Create(engine);
            definer(builder);
            builder.Build();
            return this;
        }

        public IFSMBuilderStepConstruct TransformFSMDefine(Action<IFSMTransformBuilder> definer)
        {
            var builder = FSMTransformBuilder.Create(engine);
            definer(builder);
            builder.Build();
            return this;
        }
    }
}
