using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StateMachine
{
    public class NodeEventDescription
    {
        public int Index { get; set; }

        public string Description { get; set; }
    }

    //用于Flow图显示
    public interface IVisualNode
    {
        double PosX { get; set; }

        double PosY { get; set; }

        //double OffsetX { get; set; }

        //double OffsetY { get; set; }

        string FlowID { get; set; }

        string ClassType { get; set; }

        string Color { get; set; }

        string Discription { get; set; }

        //该Node发出的事件的index及事件名
        List<NodeEventDescription> EventDescriptions { get; set; }

    }

    //设置状态转移的接口
    //用来实现状态图的结构
    public interface ITransitionContainer
    {
        //增加状态转移接线
        //@event 导致状态转移的事件
        //index 对应的分支
        //targetState 转移到的状态
        void AddTransition(FSMEvent @event, IFSMNode targetState);

        bool HasTransition(FSMEvent @event);

        bool HasTransition(IFSMNode target);

        bool DeleteTransition(FSMEvent @event);

        bool DeleteTransition(IFSMNode target);

        void ClearTransition();

        IFSMNode TargetState(FSMEvent @event);

        //获取当前状态的所有后继状态
        IEnumerable<IFSMNode> GetAllTargets();

        //遍历所有后续节点，不包括自身
        IEnumerator<IFSMNode> GetEnumerator();

        IEnumerable<FSMTransition> GetFSMTransitions();
    }

    public interface IFSMNode : ITransitionContainer, IVisualNode, IDisposable
    {
        //执行的时候从上一状态传入的上下文
        FSMNodeContext Context { get; set; }

        //当前流程的数据结构
        IExcecuterContext ExecuterContext { get; set; }

        //脚本中的命名空间
        string NamePrev { get; set; }

        //流程执行前，所有节点的初始化
        void InitBeforeStart();

        //执行
        Task<bool> GoAsync();

        //设置返回结果发起的对应事件
        void SetBranchEvent(int index, FSMEvent @event);

        //当前节点对象在脚本内的名称
        string Name { get; set; }

        //当前节点对应的异步Task
        Task WaitCurrentTask { get; }

        event Action RaiseInterrupt;
        event Action RaisePause;

        Task CreateNewAsync();
        Task ExitStateAsync();
    }

    //初始化上下文与执行上下文使用相同类型
    public interface IFSMNode<T> : IFSMNode where T : class
    {
        //执行的时候从上一状态传入的上下文
        new FSMNodeContext<T> Context { get; set; }
    }

}
