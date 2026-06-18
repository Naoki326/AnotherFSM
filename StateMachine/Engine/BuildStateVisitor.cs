using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace StateMachine
{
    internal class BuildStateVisitor : StateMachineScriptBaseVisitor<object>
    {
        private IFSMNodeFactory nodeFactory;
        private FSMEngine engine;

        public BuildStateVisitor(Dictionary<string, FSMEvent> eventDict, Dictionary<string, IFSMNode> nodeDict, IFSMNodeFactory nodeFactory, FSMEngine engine)
        {
            this.nodeFactory = nodeFactory;
            this.engine = engine;
            EventDict = eventDict;
            NodeDict = nodeDict;
        }

        private IFSMNode node = default!;
        public Dictionary<string, FSMEvent> EventDict;
        public Dictionary<string, IFSMNode> NodeDict;

        // ---- 模块实例展开状态 ----
        private FsmModuleInstance? currentModuleInstance;
        private Dictionary<string, string> currentOutputMap = new();
        private string currentModulePrefix = "";

        // ---- 模块定义解析状态 ----
        private List<string> currentModuleInputs = new();
        private List<string> currentModuleOutputs = new();
        private List<string> currentModuleTerminals = new();
        private List<TemplateTransition>? currentModuleTransitions;
        // 是否正在解析模块定义（此时不应展开内层模块实例）
        private bool isParsingModuleDefinition = false;

        private readonly HashSet<string> sharedLoadingStack = new(StringComparer.OrdinalIgnoreCase);

        private string nameprev = "";

        public override object? VisitNamespace([NotNull] StateMachineScriptParser.NamespaceContext context)
        {
            nameprev = context.STRING().ToString() + ".";
            foreach (var c in context.expression())
            {
                Visit(c);
            }
            nameprev = "";
            return null;
        }

        // ====================== Import 语句处理 ======================

        public override object VisitImport_statement([NotNull] StateMachineScriptParser.Import_statementContext context)
        {
            var strings = context.STRING();
            string importPath = strings[0].GetText();
            string moduleName = strings.Length >= 2 ? strings[1].GetText() : importPath;

            engine.LoadModuleWithDependencies(importPath, engine.CurrentScriptDirectory ?? ".", sharedLoadingStack);

            // 如使用了 as 别名，以别名注册模块引用
            if (strings.Length >= 2 && moduleName != importPath)
            {
                if (engine.TryGetModule(importPath, out var info))
                {
                    if (!engine.ContainsModule(moduleName))
                    {
                        engine.moduleRegistry[moduleName] = info;
                    }
                }
            }

            return null;
        }

        // ====================== Module 定义解析 ======================

        public override object VisitModule_statement([NotNull] StateMachineScriptParser.Module_statementContext context)
        {
            var strings = context.STRING();
            string moduleName = strings[0].GetText();
            string registryName = strings.Length >= 2 ? strings[1].GetText() : moduleName;

            // 保存当前状态
            var savedNodeDict = NodeDict;
            var savedEventDict = EventDict;
            var savedNameprev = nameprev;
            var savedInputs = currentModuleInputs;
            var savedOutputs = currentModuleOutputs;
            var savedTerminals = currentModuleTerminals;
            var savedTransitions = currentModuleTransitions;
            var savedIsParsingModule = isParsingModuleDefinition;

            // 切换到模块局部作用域
            var moduleNodeDict = new Dictionary<string, IFSMNode>();
            var moduleEventDict = new Dictionary<string, FSMEvent>();
            currentModuleInputs = new List<string>();
            currentModuleOutputs = new List<string>();
            currentModuleTerminals = new List<string>();
            currentModuleTransitions = new List<TemplateTransition>();

            // 进入模块定义解析模式（阻止内层模块展开）
            isParsingModuleDefinition = true;
            NodeDict = moduleNodeDict;
            EventDict = moduleEventDict;
            nameprev = "";

            try
            {
                // 访问 module_body
                Visit(context.module_body());
            }
            finally
            {
                // 恢复（异常时也恢复，防止标志粘滞）
                NodeDict = savedNodeDict;
                EventDict = savedEventDict;
                nameprev = savedNameprev;
                isParsingModuleDefinition = savedIsParsingModule;
            }

            // 构建 FsmModuleInfo
            var moduleInfo = new FsmModuleInfo
            {
                ModuleName = registryName,
                Inputs = currentModuleInputs,
                Outputs = currentModuleOutputs,
                Terminals = currentModuleTerminals,
                TemplateNodes = moduleNodeDict,
                TemplateEvents = moduleEventDict,
                PendingTransitions = currentModuleTransitions ?? new List<TemplateTransition>(),
            };

            // 写入 engine.moduleRegistry
            engine.moduleRegistry[registryName] = moduleInfo;

            // 恢复其他状态
            currentModuleInputs = savedInputs;
            currentModuleOutputs = savedOutputs;
            currentModuleTerminals = savedTerminals;
            currentModuleTransitions = savedTransitions;

            return null;
        }

        public override object VisitModule_body([NotNull] StateMachineScriptParser.Module_bodyContext context)
        {
            for (int i = 0; i < context.ChildCount; i++)
            {
                Visit(context.GetChild(i));
            }
            return null;
        }

        public override object VisitInput_declaration([NotNull] StateMachineScriptParser.Input_declarationContext context)
        {
            foreach (var s in context.STRING())
            {
                currentModuleInputs.Add(s.GetText());
            }
            return null;
        }

        public override object VisitOutput_declaration([NotNull] StateMachineScriptParser.Output_declarationContext context)
        {
            foreach (var s in context.STRING())
            {
                currentModuleOutputs.Add(s.GetText());
            }
            return null;
        }

        public override object VisitTerminal_declaration([NotNull] StateMachineScriptParser.Terminal_declarationContext context)
        {
            foreach (var s in context.STRING())
            {
                currentModuleTerminals.Add(s.GetText());
            }
            return null;
        }

        // 模块定义内部连线收集（仅记录，不创建实际连线）
        public override object VisitDefTransition([NotNull] StateMachineScriptParser.DefTransitionContext context)
        {
            if (currentModuleTransitions != null)
            {
                var event_name = nameprev + context.STRING()[0].GetText();
                var sourceState_name = nameprev + context.STRING()[1].GetText();
                var targetState_name = nameprev + context.STRING()[2].GetText();

                currentModuleTransitions.Add(new TemplateTransition
                {
                    SourceNode = sourceState_name,
                    EventName = event_name,
                    TargetNode = targetState_name
                });
            }
            return null;
        }

        // ====================== DefBranch（支持三种模式） ======================

        public override object VisitDefBranch([NotNull] StateMachineScriptParser.DefBranchContext context)
        {
            var index = int.Parse(context.INT().GetText());
            var event_name_raw = context.STRING().GetText();
            var event_name = nameprev + event_name_raw;

            if (currentModuleInstance != null && node == null)
            {
                // 模式1：解析模块实例 body 中的 output 映射
                // 语法 branch INT -> externalEvent 表示 output[INT] 映射到 externalEvent
                string extEventName = event_name; // 含 nameprev 前缀，与 VisitDefBranch2 一致

                if (engine.TryGetModule(currentModuleInstance.ModuleName, out var template))
                {
                    if (index >= 0 && index < template.Outputs.Count)
                    {
                        string outputName = template.Outputs[index];
                        currentOutputMap[outputName] = extEventName;
                        currentModuleInstance.OutputEventMap[outputName] = extEventName;
                    }
                    else
                    {
                        throw new ScriptException($"模块实例 {currentModuleInstance.InstanceName} output 映射出错, 索引 {index} 超出模块 {template.ModuleName} 的 output 数量 {template.Outputs.Count}");
                    }
                }
                else
                {
                    throw new ScriptException($"模块实例 {currentModuleInstance.InstanceName} 引用的模块 {currentModuleInstance.ModuleName} 未注册");
                }
            }
            else if (currentModuleInstance != null && node != null)
            {
                // 模式2：模块实例展开中 — 翻译节点的分支事件
                string finalEventName;

                if (currentOutputMap.TryGetValue(event_name_raw, out string? mappedEvent))
                {
                    // output 事件已映射
                    finalEventName = mappedEvent;
                }
                else
                {
                    // 内部事件加前缀
                    finalEventName = currentModulePrefix + event_name_raw;
                }

                if (!EventDict.TryGetValue(finalEventName, out FSMEvent value))
                {
                    value = new FSMEvent(finalEventName);
                    EventDict.Add(finalEventName, value);
                }
                node.SetBranchEvent(index, value);

                // 记录发出 output 事件的节点
                if (currentOutputMap.TryGetValue(event_name_raw, out string? extEvent))
                {
                    if (!currentModuleInstance.ExternalEventToInternalNodes.ContainsKey(extEvent))
                    {
                        currentModuleInstance.ExternalEventToInternalNodes[extEvent] = new List<string>();
                    }
                    if (!currentModuleInstance.ExternalEventToInternalNodes[extEvent].Contains(node.Name))
                    {
                        currentModuleInstance.ExternalEventToInternalNodes[extEvent].Add(node.Name);
                    }
                }
            }
            else
            {
                // 模式3：普通模式（含模块定义解析 — EventDict 已被替换为局部 dict）
                if (!EventDict.TryGetValue(event_name, out FSMEvent value))
                {
                    value = new FSMEvent(event_name);
                    EventDict.Add(event_name, value);
                }
                node.SetBranchEvent(index, value);
            }

            return base.VisitDefBranch(context);
        }

        public override object VisitDefBranch2([NotNull] StateMachineScriptParser.DefBranch2Context context)
        {
            var index = 0;
            switch (context.branch_type.Type)
            {
                case StateMachineScriptParser.INT:
                    index = int.Parse(context.INT().GetText());
                    break;
                case StateMachineScriptParser.NONE:
                    index = 0;
                    break;
                case StateMachineScriptParser.SUCCESS:
                    index = 1;
                    break;
                case StateMachineScriptParser.FAILED:
                    index = 2;
                    break;
                case StateMachineScriptParser.ERROR:
                    index = 3;
                    break;
                case StateMachineScriptParser.BREAK:
                    index = 4;
                    break;
                case StateMachineScriptParser.CANCEL:
                    index = 5;
                    break;
            }

            var event_name_raw = context.STRING().GetText();
            var event_name = nameprev + event_name_raw;

            if (currentModuleInstance != null && node == null)
            {
                // 模块实例 body 中的 output 映射
                string extEventName = event_name; // 含 nameprev 前缀
                if (engine.TryGetModule(currentModuleInstance.ModuleName, out var template))
                {
                    if (index >= 0 && index < template.Outputs.Count)
                    {
                        string outputName = template.Outputs[index];
                        currentOutputMap[outputName] = extEventName;
                        currentModuleInstance.OutputEventMap[outputName] = extEventName;
                    }
                    else
                    {
                        throw new ScriptException($"模块实例 {currentModuleInstance.InstanceName} output 映射出错, 索引 {index} 超出模块 {template.ModuleName} 的 output 数量 {template.Outputs.Count}");
                    }
                }
                else
                {
                    throw new ScriptException($"模块实例 {currentModuleInstance.InstanceName} 引用的模块 {currentModuleInstance.ModuleName} 未注册");
                }
            }
            else if (currentModuleInstance != null && node != null)
            {
                // 模块实例展开中
                string finalEventName;
                if (currentOutputMap.TryGetValue(event_name_raw, out string? mappedEvent))
                {
                    finalEventName = mappedEvent;
                }
                else
                {
                    finalEventName = currentModulePrefix + event_name_raw;
                }

                if (!EventDict.TryGetValue(finalEventName, out FSMEvent value))
                {
                    value = new FSMEvent(finalEventName);
                    EventDict.Add(finalEventName, value);
                }
                node.SetBranchEvent(index, value);

                // 记录发出 output 事件的节点
                if (currentOutputMap.TryGetValue(event_name_raw, out string? extEvent2))
                {
                    if (!currentModuleInstance.ExternalEventToInternalNodes.ContainsKey(extEvent2))
                    {
                        currentModuleInstance.ExternalEventToInternalNodes[extEvent2] = new List<string>();
                    }
                    if (!currentModuleInstance.ExternalEventToInternalNodes[extEvent2].Contains(node.Name))
                    {
                        currentModuleInstance.ExternalEventToInternalNodes[extEvent2].Add(node.Name);
                    }
                }
            }
            else
            {
                // 普通模式
                if (!EventDict.TryGetValue(event_name, out FSMEvent value))
                {
                    value = new FSMEvent(event_name);
                    EventDict.Add(event_name, value);
                }
                node.SetBranchEvent(index, value);
            }

            return base.VisitDefBranch2(context);
        }

        public override object VisitNamedOutputMapDef([NotNull] StateMachineScriptParser.NamedOutputMapDefContext context)
        {
            if (currentModuleInstance == null || node != null)
                throw new ScriptException($"{context.STRING()[0].GetText()}->{context.STRING()[1].GetText()} 只能用于模块实例的 output 映射");

            var outputName = context.STRING()[0].GetText();
            var extEventName = nameprev + context.STRING()[1].GetText();

            if (!engine.TryGetModule(currentModuleInstance.ModuleName, out var template))
                throw new ScriptException($"模块实例 {currentModuleInstance.InstanceName} 引用的模块 {currentModuleInstance.ModuleName} 未注册");

            if (!template.Outputs.Any(output => string.Equals(output, outputName, StringComparison.OrdinalIgnoreCase)))
                throw new ScriptException($"模块实例 {currentModuleInstance.InstanceName} output 映射出错, 模块 {template.ModuleName} 未声明 output {outputName}");

            currentOutputMap[outputName] = extEventName;
            currentModuleInstance.OutputEventMap[outputName] = extEventName;
            return null;
        }

        // ====================== GroudFSMDef（支持模块内前缀） ======================

        public override object VisitGroudFSMDef([NotNull] StateMachineScriptParser.GroudFSMDefContext context)
        {
            if (context.STRING().Length == 2)
            {
                string startNodeName = context.STRING()[0].ToString();
                string endEventName = context.STRING()[1].ToString();

                if (currentModuleInstance != null)
                {
                    startNodeName = currentModulePrefix + startNodeName;
                    endEventName = currentModulePrefix + endEventName;
                }

                if (node is IGroupNode gnode)
                {
                    gnode.StartNode = startNodeName;
                    gnode.EndEvent = endEventName;
                }
                else if (node is IParallelNode pnode)
                {
                    pnode.FSMs.Add(new FSMDescribe()
                    {
                        StartNode = startNodeName,
                        EndEvent = endEventName,
                    });
                }
            }
            return base.VisitGroudFSMDef(context);
        }

        // ====================== 视觉属性（支持写入模块实例） ======================

        public override object VisitPOSX([NotNull] StateMachineScriptParser.POSXContext context)
        {
            double px = 0;
            if (context.DOUBLE() is null)
            {
                int.TryParse(context.INT().ToString(), out int ipx);
                px = ipx;
            }
            else
            {
                double.TryParse(context.DOUBLE().ToString(), out double dpx);
                px = dpx;
            }

            if (currentModuleInstance != null && node == null)
            {
                currentModuleInstance.PosX = px;
            }
            else if (node != null)
            {
                node.PosX = px;
            }
            return base.VisitPOSX(context);
        }

        public override object VisitPOSY([NotNull] StateMachineScriptParser.POSYContext context)
        {
            double py = 0;
            if (context.DOUBLE() is null)
            {
                int.TryParse(context.INT().ToString(), out int ipy);
                py = ipy;
            }
            else
            {
                double.TryParse(context.DOUBLE().ToString(), out double dpy);
                py = dpy;
            }

            if (currentModuleInstance != null && node == null)
            {
                currentModuleInstance.PosY = py;
            }
            else if (node != null)
            {
                node.PosY = py;
            }
            return base.VisitPOSY(context);
        }

        public override object VisitFlowIDDef([NotNull] StateMachineScriptParser.FlowIDDefContext context)
        {
            string flowId = "";
            if (context.GUID() != null)
            {
                flowId = context.GUID().GetText();
            }
            else if (context.INT() != null)
            {
                flowId = context.INT().GetText();
            }
            else if (context.STRING() != null)
            {
                flowId = context.STRING().GetText();
            }

            if (currentModuleInstance != null && node == null)
            {
                currentModuleInstance.FlowID = flowId;
            }
            else if (node != null)
            {
                node.FlowID = flowId;
            }
            return base.VisitFlowIDDef(context);
        }

        public override object VisitColorDef([NotNull] StateMachineScriptParser.ColorDefContext context)
        {
            string color = context.CODESTRING().GetText().Replace(@"""", "");

            if (currentModuleInstance != null && node == null)
            {
                currentModuleInstance.Color = color;
            }
            else if (node != null)
            {
                node.Color = color;
            }
            return base.VisitColorDef(context);
        }

        // ====================== 事件定义 ======================

        public override object VisitDefEvent([NotNull] StateMachineScriptParser.DefEventContext context)
        {
            var event_name = nameprev + context.STRING().GetText();
            if (!EventDict.ContainsKey(event_name))
            {
                EventDict.Add(event_name, new FSMEvent(event_name));
            }
            return base.VisitDefEvent(context);
        }

        // ====================== DefState / DefState2（含模块实例检测） ======================

        public override object? VisitDefState([NotNull] StateMachineScriptParser.DefStateContext context)
        {
            var state_name = nameprev + context.STRING()[0].GetText();
            string state_type = context.STRING()[0].GetText();
            if (context.STRING().Length == 2)
                state_type = context.STRING()[1].GetText();

            if (NodeDict.ContainsKey(state_name))
            {
                throw new ScriptException("State " + state_name + " 定义出错, " + "已存在相同名字的State！");
            }

            // 检测是否为模块实例
            if (!isParsingModuleDefinition && currentModuleInstance == null && engine.TryGetModule(state_type, out var moduleInfo))
            {
                return ExpandModuleInstance(state_name, moduleInfo, context);
            }

            node = CreateNode(state_type);
            node.Name = state_name;
            node.NamePrefix = nameprev;

            NodeDict.Add(state_name, node);
            foreach (var branch in context.state_branch())
            {
                Visit(branch);
            }
            node = default!;
            return null;
        }

        public override object? VisitDefState2([NotNull] StateMachineScriptParser.DefState2Context context)
        {
            var state_name = nameprev + context.STRING()[0].GetText();
            string state_type = context.STRING()[0].GetText();
            if (context.STRING().Length == 2)
                state_type = context.STRING()[1].GetText();

            if (NodeDict.ContainsKey(state_name))
            {
                throw new ScriptException("State " + state_name + " 定义出错, " + "已存在相同名字的State！");
            }

            // 检测是否为模块实例
            if (!isParsingModuleDefinition && currentModuleInstance == null && engine.TryGetModule(state_type, out var moduleInfo))
            {
                return ExpandModuleInstance(state_name, moduleInfo, context);
            }

            node = CreateNode(state_type);
            node.Name = state_name;
            node.NamePrefix = nameprev;

            NodeDict.Add(state_name, node);
            foreach (var branch in context.state_branch())
            {
                Visit(branch);
            }
            node = default!;
            return null;
        }

        // ====================== 模块实例展开 ======================

        /// <summary>
        /// 展开模块实例：解析 body 的 output 映射和视觉属性，
        /// 根据模板创建带前缀的节点和事件，并注册到 engine。
        /// </summary>
        private object? ExpandModuleInstance(string instanceName, FsmModuleInfo template, ParserRuleContext context)
        {
            var instance = new FsmModuleInstance
            {
                InstanceName = instanceName,
                ModuleName = template.ModuleName,
            };

            // 保存状态
            var savedInstance = currentModuleInstance;
            var savedOutputMap = currentOutputMap;
            var savedPrefix = currentModulePrefix;
            var savedNode = node;

            // 进入模块实例展开状态
            currentModuleInstance = instance;
            currentOutputMap = new Dictionary<string, string>();
            currentModulePrefix = instanceName + ".";
            node = default!; // Phase 1: node=null，body 分支将被解释为 output 映射

            try
            {
                // Phase 1：解析 body 收集 output 映射和视觉属性
                VisitStateBranches(context);

                // 确保模板节点的 EventDescriptions 是最新的
                foreach (var kvp in template.TemplateNodes)
                    kvp.Value.UpdateEventDescriptions();

                // Phase 2：展开模板节点
                currentModuleInstance.InputNodeNames.Clear();
                currentModuleInstance.TerminalNodeNames.Clear();
                foreach (var kvp in template.TemplateNodes)
                {
                    var templateNodeName = kvp.Key;
                    var templateNode = kvp.Value;
                    var newNodeName = currentModulePrefix + templateNodeName;

                    if (NodeDict.ContainsKey(newNodeName))
                    {
                        throw new ScriptException("State " + newNodeName + " 定义出错, 已存在相同名字的State！");
                    }

                    var newNode = CreateNode(templateNode.ClassType);
                    newNode.Name = newNodeName;
                    newNode.NamePrefix = nameprev;
                    newNode.PosX = templateNode.PosX;
                    newNode.PosY = templateNode.PosY;
                    newNode.Color = templateNode.Color;
                    newNode.FlowID = Guid.NewGuid().ToString();

                    // 复制 GroupNode/ParallelNode 信息（带前缀）
                    CopyGroupNodeInfo(templateNode, newNode);

                    node = newNode;
                    NodeDict.Add(newNodeName, newNode);

                    // 应用模板分支事件（带 output 映射 / 前缀）
                    ApplyTemplateBranches(templateNode, newNode);

                    if (template.Inputs.Contains(templateNodeName, StringComparer.OrdinalIgnoreCase))
                        currentModuleInstance.InputNodeNames.Add(newNodeName);
                    if (template.Terminals.Contains(templateNodeName, StringComparer.OrdinalIgnoreCase))
                        currentModuleInstance.TerminalNodeNames.Add(newNodeName);

                    node = default!;
                }

                // Phase 3 前：检测 output 映射值是否与某个内部事件展开后的全名碰撞，
                // 碰撞会让两者合并为同一个 FSMEvent，产生跨边界联动。
                var internalEventFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in template.TemplateEvents.Keys)
                {
                    if (!template.Outputs.Contains(name))
                        internalEventFullNames.Add(currentModulePrefix + name);
                }
                foreach (var mappedPair in currentOutputMap)
                {
                    if (internalEventFullNames.Contains(mappedPair.Value))
                    {
                        throw new ScriptException(
                            $"模块实例 {instanceName} 的 output 映射出错, 事件 {mappedPair.Value} 与模块内部事件全名碰撞, 请改用不同的外部事件名");
                    }
                }

                // Phase 3：展开模板事件
                foreach (var kvp in template.TemplateEvents)
                {
                    var eventName = kvp.Key;
                    if (template.Outputs.Contains(eventName) && currentOutputMap.TryGetValue(eventName, out var mappedEvent))
                    {
                        if (!EventDict.ContainsKey(mappedEvent))
                            EventDict.Add(mappedEvent, new FSMEvent(mappedEvent));
                    }
                    else
                    {
                        var newEventName = currentModulePrefix + eventName;
                        if (!EventDict.ContainsKey(newEventName))
                            EventDict.Add(newEventName, new FSMEvent(newEventName));
                    }
                }

                // Phase 4：展开模板连线（创建模块实例内部的 transition）
                foreach (var trans in template.PendingTransitions)
                {
                    var sourceNodeName = currentModulePrefix + trans.SourceNode;
                    var targetNodeName = currentModulePrefix + trans.TargetNode;

                    string eventName;
                    if (template.Outputs.Contains(trans.EventName) && currentOutputMap.TryGetValue(trans.EventName, out var mappedEvent))
                    {
                        eventName = mappedEvent;
                    }
                    else
                    {
                        eventName = currentModulePrefix + trans.EventName;
                    }

                    if (!EventDict.TryGetValue(eventName, out FSMEvent? fsmEvent))
                    {
                        fsmEvent = new FSMEvent(eventName);
                        EventDict.Add(eventName, fsmEvent);
                    }

                    if (!NodeDict.TryGetValue(sourceNodeName, out IFSMNode? sourceNode))
                    {
                        throw new ScriptException("State " + sourceNodeName + " 连线出错, 未定义该State！");
                    }
                    if (!NodeDict.TryGetValue(targetNodeName, out IFSMNode? targetNode))
                    {
                        throw new ScriptException("State " + targetNodeName + " 连线出错, 未定义该State！");
                    }
                    if (sourceNode.HasTransition(fsmEvent))
                    {
                        throw new ScriptException("State " + targetNodeName + " 向 " + eventName + " 连线出错, 已定义从该状态到该事件连线！");
                    }

                    sourceNode.AddTransition(fsmEvent, targetNode);
                }
            }
            finally
            {
                currentModuleInstance = savedInstance;
                currentOutputMap = savedOutputMap;
                currentModulePrefix = savedPrefix;
                node = savedNode;
            }

            // 加入 engine.moduleInstances
            engine.moduleInstances[instanceName] = instance;

            return null;
        }

        private void VisitStateBranches(ParserRuleContext context)
        {
            if (context is StateMachineScriptParser.DefStateContext defState)
            {
                var branches = defState.state_branch();
                for (int i = 0; i < branches.Length; i++)
                {
                    Visit(branches[i]);
                }
            }
            else if (context is StateMachineScriptParser.DefState2Context defState2)
            {
                var branches = defState2.state_branch();
                for (int i = 0; i < branches.Length; i++)
                {
                    Visit(branches[i]);
                }
            }
        }

        private void CopyGroupNodeInfo(IFSMNode templateNode, IFSMNode newNode)
        {
            if (newNode is IGroupNode gn && templateNode is IGroupNode tGn)
            {
                if (!string.IsNullOrEmpty(tGn.StartNode))
                    gn.StartNode = currentModulePrefix + tGn.StartNode;
                if (!string.IsNullOrEmpty(tGn.EndEvent))
                    gn.EndEvent = currentModulePrefix + tGn.EndEvent;
            }
            if (newNode is IParallelNode pn && templateNode is IParallelNode tPn)
            {
                foreach (var fsm in tPn.FSMs)
                {
                    pn.FSMs.Add(new FSMDescribe
                    {
                        StartNode = currentModulePrefix + fsm.StartNode,
                        EndEvent = currentModulePrefix + fsm.EndEvent,
                    });
                }
            }
        }

        private void ApplyTemplateBranches(IFSMNode templateNode, IFSMNode newNode)
        {
            foreach (var desc in templateNode.EventDescriptions)
            {
                string eventName = desc.Description;
                int index = desc.Index;

                string finalEventName;
                if (currentOutputMap.TryGetValue(eventName, out string? mappedEvent))
                {
                    finalEventName = mappedEvent;
                }
                else
                {
                    finalEventName = currentModulePrefix + eventName;
                }

                if (!EventDict.TryGetValue(finalEventName, out FSMEvent value))
                {
                    value = new FSMEvent(finalEventName);
                    EventDict.Add(finalEventName, value);
                }
                newNode.SetBranchEvent(index, value);

                // 记录发出 output 事件的节点
                if (currentOutputMap.TryGetValue(eventName, out string? extEvent))
                {
                    if (!currentModuleInstance.ExternalEventToInternalNodes.ContainsKey(extEvent))
                    {
                        currentModuleInstance.ExternalEventToInternalNodes[extEvent] = new List<string>();
                    }
                    if (!currentModuleInstance.ExternalEventToInternalNodes[extEvent].Contains(newNode.Name))
                    {
                        currentModuleInstance.ExternalEventToInternalNodes[extEvent].Add(newNode.Name);
                    }
                }
            }
        }

        // ====================== 节点创建 ======================

        protected virtual IFSMNode CreateNode(string state_type)
        {
            IFSMNode proc = default!;
            switch (state_type.ToLower())
            {
                default:
                    try
                    {
                        proc = nodeFactory.CreateNode(state_type);
                        proc.ClassType = state_type;
                    }
                    catch (Exception)
                    { throw new ScriptException("State " + state_type + " 定义出错, " + "该State未注入IoC中！"); }
                    break;
            }
            return proc;
        }
    }
}
