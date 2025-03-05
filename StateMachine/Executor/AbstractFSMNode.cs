using System.Collections.Concurrent;
using System.Diagnostics;
using StateMachine.Interface;

namespace StateMachine
{

    //执行块的抽象类
    public abstract partial class AbstractFSMNode : ITransitionContainer
    {
        //双键字典
        private Dictionary<string, FSMTransition> transitions = new Dictionary<string, FSMTransition>();

        //一个事件只能导向一个状态
        //但是允许多个事件导向同一个状态
        void ITransitionContainer.AddTransition(FSMEvent @event, IFSMNode targetState)
        {
            transitions.Add(@event.EventID, new FSMTransition(this, @event, targetState));
        }

        IFSMNode ITransitionContainer.TargetState(FSMEvent @event)
        {
            return transitions[@event.EventID].Target;
        }

        bool ITransitionContainer.HasTransition(FSMEvent eventCode)
        {
            return transitions.ContainsKey(eventCode.EventID);
        }

        bool ITransitionContainer.HasTransition(IFSMNode node)
        {
            return transitions.Any(p => p.Value.Target.Name == node.Name);
        }

        bool ITransitionContainer.DeleteTransition(FSMEvent @event)
        {
            return transitions.Remove(@event.EventID);
        }

        bool ITransitionContainer.DeleteTransition(IFSMNode target)
        {
            foreach (var key in transitions.Where(p => p.Value.Target.Name == target.Name).Select(p => p.Key))
            {
                transitions.Remove(key);
            }
            return true;
        }

        void ITransitionContainer.ClearTransition()
        {
            transitions.Clear();
        }

        IEnumerable<IFSMNode> ITransitionContainer.GetAllTargets()
        {
            return transitions.Values.Select(p => p.Target);
        }

        private IEnumerable<IFSMNode> Traversal(IFSMNode firstNode, ConcurrentBag<IFSMNode> visited)
        {
            if (firstNode is null)
                yield break;
            foreach (var targets in firstNode.GetAllTargets())
            {
                if (!visited.Contains(targets))
                {
                    visited.Add(targets);
                    yield return targets;
                    foreach (var t in Traversal(targets, visited))
                        yield return t;
                }
            }
        }

        public IEnumerator<IFSMNode> GetEnumerator()
        {
            foreach (var t in Traversal(this, new ConcurrentBag<IFSMNode>()))
                yield return t;
        }

        public IEnumerable<FSMTransition> GetFSMTransitions()
        {
            return transitions.Values;
        }

        public FSMTransition GetFSMTransition(string target)
        {
            return transitions.First(p => p.Value.Target.Name == target).Value;
        }

        public override string ToString()
        {
            IFSMNode node = this;

            string outString =
            $$"""
            def {{node.Name}}({{node.ClassType}})
            {
            {{EventDescriptions?.Aggregate("", (p, q) => p + "\t" + q.Index + "->" + q.Description + ";\r\n") + "\t"}}Pos:({{node.PosX}}, {{node.PosY}});
                Color: "{{node.Color}}";
                Type: {{node.ClassType}};
                FlowID: {{node.FlowID}};
            }
            """ + "\r\n";
            foreach (var transition in transitions)
            {
                outString += transition.Value.ToString();
            }
            return outString + "\r\n";
        }
    }

    public abstract partial class AbstractFSMNode : IFSMNode
    {
        private Dictionary<int, FSMEvent> branchDict = [];
        void IFSMNode.SetBranchEvent(int index, FSMEvent @event)
        {
            branchDict[index] = @event;
        }

        [DebuggerNonUserCode]
        public FSMNodeContext Context { get; set; } = default!;

        private Action? raisePause;
        // 暂停针对一个具体的FSMExecute，所以需要用raisePause绑定到对应的FSMExecute
        event Action IFSMNode.RaisePause
        {
            add { raisePause += value; }
            remove { raisePause = (Action)Delegate.Remove(raisePause, value)!; }
        }

        /// <summary>
        /// 状态名称
        /// </summary>
        [FSMProperty("Name", false, true, -1)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 状态名称的前缀，类似命名空间的机制
        /// </summary>
        [FSMProperty("NamePrev", false, true, -1)]
        public string NamePrefix { get; set; } = "";

        IExcecuterContext IFSMNode.ExecuterContext { get; set; } = default!;
        /// <summary>
        /// 当前FSMExecute对象的上下文
        /// </summary>
        public IExcecuterContext ExecuterContext => (this as IFSMNode).ExecuterContext;

        [DebuggerStepThrough]
        async Task<bool> IFSMNode.RunAsync()
        {
            TaskCompletionSource<bool> tcs = new();
            waitCurrentTask = tcs.Task;
            try
            {
                Context.SetTokenSource(new CancellationTokenSource());
                Context.SetPause(false);
                await ExecuteMethodAsync();
                await FinishAsync();
                tcs.TrySetResult(true);
                return true;
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled();
                await Interupt();
                if (Context != null)
                { Context.SetPause(true); }
                return false;
            }
            finally
            {
                await ExitAsync();
                tcs.TrySetResult(false);
            }
        }

        // pause针对一个具体的FSMExecute，所以需要用raisePause绑定到对应的FSMExecute
        protected void Pause()
        {
            raisePause?.Invoke();
        }

        Task waitCurrentTask = Task.FromResult(true);
        Task IFSMNode.WaitCurrentTask => waitCurrentTask;

        public double PosX { get; set; } = 0;
        public double PosY { get; set; } = 0;
        public double OffsetX { get; set; } = 0;
        public double OffsetY { get; set; } = 0;
        public string FlowID { get; set; } = Guid.NewGuid().ToString();
        public string ClassType { get; set; } = "";
        public string Color { get; set; } = "white";

        [FSMProperty("Discription", false, true, -1)]
        public string Discription { get; set; } = "";


        [FSMProperty("EventDescriptions", false, true, -1)]
        public List<NodeEventDescription> EventDescriptions { get; set; } = [];

        async Task IFSMNode.CreateNewAsync() { await RestartAsync(); }

        async Task IFSMNode.ExitStateAsync() { await ExitAsync(); }

        protected void PublishEvent(int index)
        {
            if (branchDict.TryGetValue(index, out FSMEvent value))
            { FSMEventAggregator.EventAggregator?.Publish(value); }
        }

        protected void PublishEvent<T>(int index, T eventContext)
        {
            if (branchDict.TryGetValue(index, out FSMEvent value)
                && value.Clone() is FSMEvent eventValue)
            {
                eventValue.EventContext = eventContext!;
                FSMEventAggregator.EventAggregator?.Publish(eventValue);
            }
        }

        protected void PublishEvent(int index, object eventContext)
        {
            PublishEvent<object>(index, eventContext);
        }

        protected void PublishEvent(FSMEnum pEnum)
        {
            int index = pEnum.GetHashCode();
            PublishEvent(index);
        }

        protected void PublishEvent<T>(FSMEnum pEnum, T eventContext)
        {
            int index = pEnum.GetHashCode();
            PublishEvent(index, eventContext);
        }

        protected void PublishEvent(FSMEnum pEnum, object eventContext)
        {
            int index = pEnum.GetHashCode();
            PublishEvent<object>(index, eventContext);
        }

        //启动时触发
        protected abstract Task RestartAsync();

        //退出当前节点时触发
        protected abstract Task ExitAsync();

        //执行方法
        protected abstract Task ExecuteMethodAsync();

        //完成时调用
        protected abstract Task FinishAsync();

        //暂停时的保存现场操作
        protected abstract Task Interupt();

        ~AbstractFSMNode()
        {
            Dispose(false);
        }

        void IDisposable.Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// 状态机Restart的时候调用一次，用于初始化一些服务实例
        /// </summary>
        public virtual void InitBeforeStart()
        {
        }
    }



    public abstract class AbstractFSMNode<T> : AbstractFSMNode where T : class
    {
        // 限制上下文的类型
        public new FSMNodeContext<T>? Context
        {
            get { return base.Context as FSMNodeContext<T>; }
            set { base.Context = value; }
        }
    }

}
