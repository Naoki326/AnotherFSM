using Antlr4.Runtime.Atn;
using System.Reflection;

namespace StateMachine
{

    //另一种方式创建流程结构
    public partial class FSMEngine
    {
        IFSMNodeFactory nodeFactory;

        public FSMEngine(IFSMNodeFactory nodeFactory)
        {
            this.nodeFactory = nodeFactory;
        }

        public void CreateNode(string node_type, string name, string namePrev = "")
        {
            IFSMNode proc;
            //预定义的Node
            switch (node_type.ToLower())
            {
                default:
                    try
                    {
                        proc = nodeFactory.CreateNode(node_type);
                        proc.ClassType = node_type;
                    }
                    catch (Exception)
                    { throw new ScriptException("Node " + node_type + " 定义出错, " + "该Node未注入IoC中！"); }
                    break;
            }
            if (proc is null)
                throw new ScriptException("Node " + node_type + " 获取失败！");
            proc.Name = name;
            proc.NamePrefix = namePrev;
            this.nodeDict.Add(namePrev + name, proc);
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
                        proc = nodeFactory.CreateNode(node_type);
                        proc.ClassType = node_type;
                    }
                    catch (Exception)
                    { return false; }
                    break;
            }
            if (proc is null)
                return false;
            proc.Name = name;
            proc.NamePrefix = namePrev;
            this.nodeDict.Add(namePrev + name, proc);
            return true;
        }

        public void CreateNode<T>(string name, string namePrev = "") where T : IFSMNode
        {
            IFSMNode proc = default!;
            //预定义的Node
            try
            {
                string node_type = nodeFactory.GetNodeFeatureName(typeof(T));
                proc = nodeFactory.CreateNode(node_type);
                proc.ClassType = node_type;
            }
            catch (Exception e)
            { throw new ScriptException("Node " + typeof(T) + " 定义出错, " + "该Node未注入IoC中！", e); }
            if (proc is null)
            { throw new ScriptException("Node " + typeof(T) + " 获取失败！"); }
            proc.Name = name;
            proc.NamePrefix = namePrev;
            this.nodeDict.Add(namePrev + name, proc);
            return;
        }

        public bool TryCreateNode<T>(string name, string namePrev = "") where T : IFSMNode
        {
            IFSMNode proc = default!;
            //预定义的Node
            try
            {
                string node_type = nodeFactory.GetNodeFeatureName(typeof(T));
                proc = nodeFactory.CreateNode(node_type);
                proc.ClassType = node_type;
            }
            catch (Exception)
            { return false; }
            if (proc is null)
                return false;
            proc.Name = name;
            proc.NamePrefix = namePrev;
            this.nodeDict.Add(namePrev + name, proc);
            return true;
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
            nodeDict.Remove(originName);
            nodeDict.Add(newName, node);
        }

        public bool TryChangeNodeName(string name, string newName)
        {
            if (name != newName
                && nodeDict.TryGetValue(name, out IFSMNode last))
            {
                last.Name = newName;
                nodeDict.Remove(name);
                nodeDict.Add(last.NamePrefix + newName, last);
                return true;
            }
            return false;
        }

        public void DeleteNode(string name)
        {
            this.nodeDict.Remove(name);
            return;
        }

        public bool TryDeleteNode(string name)
        {
            try
            {
                this.nodeDict.Remove(name);
            }
            catch (Exception)
            { return false; }
            return true;
        }


        public void ForceConnectNode(string eventName, string lastNode, string nextNode)
        {
            if (!eventDict.TryGetValue(eventName, out FSMEvent fseEvent))
            {
                fseEvent = new FSMEvent(eventName);
                eventDict[eventName] = fseEvent;
            }
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode last))
            {
                throw new ScriptException("Node " + lastNode + " 连线出错, " + "该Node未注入IoC中！");
            }
            if (!nodeDict.TryGetValue(nextNode, out IFSMNode next))
            {
                throw new ScriptException("Node " + nextNode + " 连线出错, " + "该Node未注入IoC中！");
            }
            if (last.HasTransition(fseEvent))
            {
                last.DeleteTransition(fseEvent);
            }

            last.AddTransition(fseEvent, next);
            return;
        }


        public bool TryForceConnectNode(string eventName, string lastNode, string nextNode)
        {
            if (!eventDict.TryGetValue(eventName, out FSMEvent fseEvent))
            {
                fseEvent = new FSMEvent(eventName);
                eventDict[eventName] = fseEvent;
            }
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode last))
            {
                return false;
            }
            if (!nodeDict.TryGetValue(nextNode, out IFSMNode next))
            {
                return false;
            }
            if (last.HasTransition(fseEvent))
            {
                last.DeleteTransition(fseEvent);
            }

            last.AddTransition(fseEvent, next);
            return true;
        }

        public void ConnectNode(string eventName, string lastNode, string nextNode)
        {
            if (!eventDict.TryGetValue(eventName, out FSMEvent fseEvent))
            {
                fseEvent = new FSMEvent(eventName);
                eventDict[eventName] = fseEvent;
            }
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode last))
            {
                throw new ScriptException("Node " + lastNode + " 连线出错, " + "该Node未注入IoC中！");
            }
            if (!nodeDict.TryGetValue(nextNode, out IFSMNode next))
            {
                throw new ScriptException("Node " + nextNode + " 连线出错, " + "该Node未注入IoC中！");
            }
            if (last.HasTransition(fseEvent))
            {
                throw new ScriptException("Node " + nextNode + " 连线出错, " + "重复接线！");
            }

            last.AddTransition(fseEvent, next);
            return;
        }

        public bool TryConnectNode(string eventName, string lastNode, string nextNode)
        {
            if (!eventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                eventDict[eventName] = fsmEvent;
            }
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode last)
                || !nodeDict.TryGetValue(nextNode, out IFSMNode next))
            {
                return false;
            }
            if (last.HasTransition(fsmEvent))
            {
                return false;
            }

            last.AddTransition(fsmEvent, next);
            return true;
        }

        public void DeleteTransition(string eventName, string lastNode)
        {
            if (!eventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                eventDict[eventName] = fsmEvent;
            }
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode last))
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
            if (!eventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                eventDict[eventName] = fsmEvent;
            }
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode last))
            {
                return false;
            }
            return last.DeleteTransition(fsmEvent);
        }

        public void ClearTransition(string lastNode)
        {
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode value))
            {
                throw new ScriptException("Node " + lastNode + " 删线出错, " + "该Node未注入IoC中！");
            }

            value.ClearTransition();
        }
        public bool TryClearTransition(string lastNode)
        {
            if (!nodeDict.TryGetValue(lastNode, out IFSMNode value))
            {
                return false;
            }

            value.ClearTransition();
            return true;
        }

        public void AttachEvent(string node, string eventName, FSMEnum branchEnum)
        {
            AttachEvent(node, eventName, (int)branchEnum);
        }
        public void AttachEvent(string node, string eventName, int branch)
        {
            if (!nodeDict.TryGetValue(node, out IFSMNode value))
            {
                throw new ScriptException("Node " + node + " 添加事件出错, " + "该Node未注入IoC中！");
            }
            if (!eventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                TryAddEvent(fsmEvent);
            }

            value.SetBranchEvent(branch, fsmEvent);
        }

        public bool TryAttachEvent(string node, string eventName, int branch)
        {
            if (!nodeDict.TryGetValue(node, out IFSMNode value))
            {
                return false;
            }
            if (!eventDict.TryGetValue(eventName, out FSMEvent fsmEvent))
            {
                fsmEvent = new FSMEvent(eventName);
                eventDict.Add(eventName, fsmEvent);
            }

            value.SetBranchEvent(branch, fsmEvent);
            return true;
        }
        public bool TryAttachEvent(string node, string eventName, FSMEnum branchEnum)
        {
            return TryAttachEvent(node, eventName, (int)branchEnum);
        }

        public void ClearNodes()
        {
            nodeDict.Clear();
        }

    }
}
