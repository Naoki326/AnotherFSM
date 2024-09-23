using StateMachine.Interface;
using System.Collections.Concurrent;
using System.Text;

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

            StringBuilder outStringB = new StringBuilder();
            outStringB.AppendLine($"def {node.Name}({node.ClassType})");
            outStringB.AppendLine($"{{");
            outStringB.Append($"{EventDescriptions?.Aggregate("", (p, q) => p + "\t" + q.Index + "->" + q.Description + ";\r\n") + "\t"}Pos:({node.PosX}, {node.PosY});\r\n");
            outStringB.AppendLine($"\tColor: \"{node.Color}\";");
            outStringB.AppendLine($"\tType: {node.ClassType};");
            outStringB.AppendLine($"\tFlowID: {node.FlowID};");
            outStringB.AppendLine($"}}");

            foreach (var transition in transitions)
            {
                outStringB.Append(transition.Value.ToString());
            }
            outStringB.AppendLine();
            return outStringB.ToString();
        }
    }

    public abstract partial class AbstractFSMNode : IFSMNode
    {
        private Dictionary<int, FSMEvent> BranchDict = new Dictionary<int, FSMEvent>();
        void IFSMNode.SetBranchEvent(int index, FSMEvent @event)
        {
            BranchDict[index] = @event;
            if (EventDescriptions.FirstOrDefault(p => p.Index == index) is NodeEventDescription description)
            {
                description.Description = @event.EventName;
            }
            else
            {
                EventDescriptions.Add(new NodeEventDescription() { Index = index, Description = @event.EventName });
            }
        }

        //状态机的上下文
        //[Obsolete("建议使用LastContext和NextContext")]
        public FSMNodeContext Context { get; set; } = default!;

        protected object LastData => Context.Data;
        protected object NextData { set => Context.Data = value; }

        private Action raiseInterrupt;
        event Action IFSMNode.RaiseInterrupt
        {
            add { raiseInterrupt += value; }
            remove { raiseInterrupt = (Action)Delegate.Remove(raiseInterrupt, value); }
        }

        private Action raisePause;
        event Action IFSMNode.RaisePause
        {
            add { raisePause += value; }
            remove { raisePause = (Action)Delegate.Remove(raiseInterrupt, value); }
        }


        [FSMProperty("Name", false, true, -1)]
        public string Name { get; set; } = "";

        [FSMProperty("NamePrev", false, true, -1)]
        public string NamePrev { get; set; } = "";

        IExcecuterContext IFSMNode.ExecuterContext { get; set; } = default!;
        //当前线程的上下文，隐藏set方法
        public IExcecuterContext ExecuterContext => (this as IFSMNode).ExecuterContext;


        async Task<bool> IFSMNode.GoAsync()
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            waitCurrentTask = tcs.Task;
            try
            {
                Context.SetTokenSource(new CancellationTokenSource());
                Context.SetPause(false);
                await ExecuteMethodAsync();
                await FinishAsync();
                return true;
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult(false);
                await Interupt();
                Context.SetPause(true);
                return false;
            }
            finally
            {
                await ExitAsync();
                tcs.TrySetResult(true);
            }
        }

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
        public List<NodeEventDescription> EventDescriptions { get; set; } = new List<NodeEventDescription>();

        async Task IFSMNode.CreateNewAsync() { await RestartAsync(); }

        async Task IFSMNode.ExitStateAsync() { await ExitAsync(); }

        protected void PublishEvent(int index)
        {
            if (BranchDict.TryGetValue(index, out FSMEvent value))
            { FSMEventAggregator.EventAggregator?.Publish(value); }
        }

        protected void PublishEvent<T>(int index, T eventContext)
        {
            if (BranchDict.TryGetValue(index, out FSMEvent value)
                && value.Clone() is FSMEvent eventValue)
            {
                eventValue.EventContext = eventContext!;
                FSMEventAggregator.EventAggregator?.Publish(eventValue);
            }
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

        protected void PublishEvent(int index, object eventContext)
        {
            PublishEvent<object>(index, eventContext);
        }

        protected void PublisEventWithInterupt(int index)
        {
            raiseInterrupt?.Invoke();
            PublishEvent(index);
        }

        protected void PublisEventWithInterupt<T>(int index, T eventContext)
        {
            raiseInterrupt?.Invoke();
            PublishEvent(index, eventContext);
        }

        protected void PublishEvent(FSMEnum pEnum, object eventContext)
        {
            PublishEvent<object>(pEnum, eventContext);
        }

        protected void PublisEventWithInterupt(FSMEnum pEnum)
        {
            raiseInterrupt?.Invoke();
            PublishEvent(pEnum);
        }

        protected void PublisEventWithInterupt<T>(FSMEnum pEnum, T eventContext)
        {
            raiseInterrupt?.Invoke();
            PublishEvent(pEnum, eventContext);
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

        protected virtual void Dispose(bool Disposing)
        {
        }

        public virtual void InitBeforeStart()
        {
        }
    }


    public abstract class AbstractFSMNode<T> : AbstractFSMNode, IFSMNode<T> where T : class
    {
        public new FSMNodeContext<T> Context
        {
            get { return base.Context as FSMNodeContext<T>; }
            set { base.Context = value; }
        }
    }

    public abstract class AbstractFSMNode<T, U> : AbstractFSMNode, IFSMNode where T : class where U : class
    {
        protected new T LastData => Context.Data as T;
        protected new U NextData { set => Context.Data = value; }
    }

}
