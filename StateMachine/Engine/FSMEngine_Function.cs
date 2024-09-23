namespace StateMachine
{
    //另一种方式创建流程结构
    public partial class FSMEngine
    {

        public void CreateNode(string node_type, string name, string namePrev = "")
        {
            IFSMNode proc;
            //预定义的Node
            switch (node_type.ToLower())
            {
                default:
                    try
                    {
                        proc = IoC.Get<IFSMNode>(node_type);
                    }
                    catch (Exception)
                    { throw new ScriptException("Node " + node_type + " 定义出错, " + "该Node未注入IoC中！"); }
                    break;
            }
            if (proc is null)
                throw new ScriptException("Node " + node_type + " 获取失败！");
            proc.Name = name;
            proc.NamePrev = namePrev;
            if (proc is BaseGroupNode bNode)
            {
                bNode.NodeStateChanged += GroupNodeStateChanged;
                bNode.NodeExitChanged += GroupNodeExitChanged;
            }
            this.NodeDict.Add(namePrev + name, proc);
            return;
        }
        public bool TryCreateNode(string node_type, string name, string namePrev = "")
        {
            IFSMNode proc;
            //预定义的Node
            switch (node_type.ToLower())
            {
                default:
                    try
                    {
                        proc = IoC.Get<IFSMNode>(node_type);
                    }
                    catch (Exception)
                    { return false; }
                    break;
            }
            if (proc is null)
                return false;
            proc.Name = name;
            proc.NamePrev = namePrev;
            this.NodeDict.Add(namePrev + name, proc);
            return true;
        }

        public void CreateNode<T>(string name, string namePrev = "") where T : IFSMNode
        {
            IFSMNode proc = default;
            //预定义的Node
            try
            {
                proc = IoC.Get<IFSMNode>(typeof(T).Name);
            }
            catch (Exception e)
            { throw new ScriptException("Node " + typeof(T) + " 定义出错, " + "该Node未注入IoC中！", e); }
            if (proc is null)
            { throw new ScriptException("Node " + typeof(T) + " 获取失败！"); }
            proc.Name = name;
            proc.NamePrev = namePrev;
            this.NodeDict.Add(namePrev + name, proc);
            return;
        }

        public bool TryCreateNode<T>(string name, string namePrev = "") where T : IFSMNode
        {
            IFSMNode proc = default;
            //预定义的Node
            try
            {
                proc = IoC.Get<IFSMNode>(typeof(T).Name);
            }
            catch (Exception)
            { return false; }
            if (proc is null)
                return false;
            proc.Name = name;
            proc.NamePrev = namePrev;
            this.NodeDict.Add(namePrev + name, proc);
            return true;
        }

        public void ChangeTransitionName(string lastNode, string nextNode, string originEventName, string newEventName)
        {
            DeleteTransition(originEventName, lastNode);
            ConnectNode(newEventName, lastNode, nextNode);
        }

        public void ChangeNodeName(string originName, string newName)
        {
            if (!TryGetNode(originName, out IFSMNode node))
            {
                throw new FSMException($"{originName} not found!");
            }

            if (TryGetNode(newName, out _))
            {
                throw new FSMException($"{newName} is exist!");
            }

            node.Name = newName;
            NodeDict.Remove(originName);
            NodeDict.Add(newName, node);
        }

        public bool TryChangeNodeName(string name, string newName)
        {
            if (name != newName
                && NodeDict.TryGetValue(name, out IFSMNode last))
            {
                last.Name = newName;
                NodeDict.Remove(name);
                NodeDict.Add(last.NamePrev + newName, last);
                return true;
            }
            return false;
        }

        public void DeleteNode(string name)
        {
            if (NodeDict[name] is BaseGroupNode bNode)
            {
                bNode.NodeStateChanged -= GroupNodeStateChanged;
                bNode.NodeExitChanged -= GroupNodeExitChanged;
            }
            this.NodeDict.Remove(name);
            return;
        }

        public bool TryDeleteNode(string name)
        {
            try
            {
                this.NodeDict.Remove(name);
            }
            catch (Exception)
            { return false; }
            return true;
        }

        public void ConnectNode(string eventName, string lastNode, string nextNode)
        {
            if (!EventDict.TryGetValue(eventName, out FSMEvent fseEvent))
            {
                fseEvent = new FSMEvent(eventName);
                EventDict[eventName] = fseEvent;
            }
            if (!NodeDict.TryGetValue(lastNode, out IFSMNode last))
            {
                throw new ScriptException("Node " + lastNode + " 连线出错, " + "该Node未注入IoC中！");
            }
            if (!NodeDict.TryGetValue(nextNode, out IFSMNode next))
            {
                throw new ScriptException("Node " + nextNode + " 连线出错, " + "该Node未注入IoC中！");
            }
            if (!last.HasTransition(fseEvent))
            {
                throw new ScriptException("Node " + nextNode + " 连线出错, " + "重复接线！");
            }

            last.AddTransition(fseEvent, next);
            return;
        }

        public bool TryConnectNode(string eventName, string lastNode, string nextNode)
        {
            if (!EventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                EventDict[eventName] = fsmEvent;
            }
            if (!NodeDict.TryGetValue(lastNode, out IFSMNode last)
                || !NodeDict.TryGetValue(nextNode, out IFSMNode next))
            {
                return false;
            }
            if (!last.HasTransition(fsmEvent))
            {
                return false;
            }

            last.AddTransition(fsmEvent, next);
            return true;
        }

        public void DeleteTransition(string eventName, string lastNode)
        {
            if (!EventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                EventDict[eventName] = fsmEvent;
            }
            if (!NodeDict.TryGetValue(lastNode, out IFSMNode last))
            {
                throw new ScriptException("Node " + lastNode + " 删线出错, " + "该Node未注入IoC中！");
            }
            if (!last.DeleteTransition(fsmEvent))
            {
                throw new ScriptException("Node " + lastNode + $" 删线出错, 该Node无{eventName}事件！");
            }

        }
        public bool TryDeleteTransition(string eventName, string lastNode)
        {
            if (!EventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                EventDict[eventName] = fsmEvent;
            }
            if (!NodeDict.TryGetValue(lastNode, out IFSMNode last))
            {
                return false;
            }
            return last.DeleteTransition(fsmEvent);
        }

        public void ClearTransition(string lastNode)
        {
            if (!NodeDict.TryGetValue(lastNode, out IFSMNode value))
            {
                throw new ScriptException("Node " + lastNode + " 删线出错, " + "该Node未注入IoC中！");
            }

            value.ClearTransition();
        }
        public bool TryClearTransition(string lastNode)
        {
            if (!NodeDict.TryGetValue(lastNode, out IFSMNode value))
            {
                return false;
            }

            value.ClearTransition();
            return true;
        }

        public void AddEvent(string node, string eventName, FSMEnum branchEnum)
        {
            AddEvent(node, eventName, (int)branchEnum);
        }
        public void AddEvent(string node, string eventName, int branch)
        {
            if (!NodeDict.TryGetValue(node, out IFSMNode value))
            {
                throw new ScriptException("Node " + node + " 添加事件出错, " + "该Node未注入IoC中！");
            }
            if (!EventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                EventDict.Add(eventName, fsmEvent);
            }

            value.SetBranchEvent(branch, fsmEvent);
        }

        public bool TryAddEvent(string node, string eventName, int branch)
        {
            if (!NodeDict.TryGetValue(node, out IFSMNode value))
            {
                return false;
            }
            if (!EventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                EventDict.Add(eventName, fsmEvent);
            }

            value.SetBranchEvent(branch, fsmEvent);
            return true;
        }
        public bool TryAddEvent(string node, string eventName, FSMEnum branchEnum)
        {
            return TryAddEvent(node, eventName, (int)branchEnum);
        }

        public void ClearNodes()
        {
            NodeDict.Clear();
        }

        public void ClearEvents()
        {
            EventDict.Clear();
        }

    }
}
