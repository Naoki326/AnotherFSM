using StateMachine;

namespace StateMachineTest.TestInfrastructure;

/// <summary>
/// 一个最小化的 <see cref="IFSMNode"/> 测试替身（test double）。
/// </summary>
/// <remarks>
/// 它存在的目的只有一个：让那些"需要一个节点对象存在、但并不真正执行它"的测试
/// （例如验证 <c>FSMEngine.CreateNode</c> / <c>ContainsNode</c>）能跑起来。
/// <para>
/// <c>FSMEngine.CreateNode</c> 只会设置 <see cref="ClassType"/> / <see cref="Name"/> /
/// <see cref="NamePrefix"/>，所以这三个属性是"真"的；其余成员全部是无副作用的桩
/// （返回默认值 / 空集合 / 已完成的 Task）。
/// </para>
/// <para>
/// 本替身额外实现 <see cref="IFSMEngineInjectable"/>，并把注入的引擎引用记到
/// <see cref="ReceivedEngine"/>，供接线相关的特征化测试观察。
/// </para>
/// </remarks>
public sealed class FakeNode : IFSMNode, IFSMEngineInjectable, IFSMContextNode
{
    public Type? ContextType => null; // Graph-only fixture: explicitly no business dependency.
    // ---------- IVisualNode ----------
    public string NamePrefix { get; set; } = "";
    public string Name { get; set; } = "";
    public double PosX { get; set; }
    public double PosY { get; set; }
    public string FlowID { get; set; } = "";
    public string ClassType { get; set; } = "";
    public string Color { get; set; } = "";
    public string Discription { get; set; } = "";
    public List<NodeEventDescription> EventDescriptions { get; set; } = new();
    public void UpdateEventDescriptions() { }

    // ---------- ITransitionContainer ----------
    // 记录转移，供 ConnectNode 相关测试观察（替身随测试需要而"长出"能力）
    private readonly Dictionary<FSMEvent, IFSMNode> _transitions = new();
    public void AddTransition(FSMEvent @event, IFSMNode targetState) => _transitions[@event] = targetState;
    public bool HasTransition(FSMEvent @event) => _transitions.ContainsKey(@event);
    public bool HasTransition(IFSMNode target) => _transitions.ContainsValue(target);
    public bool DeleteTransition(FSMEvent @event) => _transitions.Remove(@event);
    public bool DeleteTransition(IFSMNode target)
    {
        var key = _transitions.FirstOrDefault(kv => kv.Value == target).Key;
        return key is not null && _transitions.Remove(key);
    }
    public void ClearTransition() => _transitions.Clear();
    public IFSMNode TargetState(FSMEvent @event)
        => _transitions.TryGetValue(@event, out var t) ? t : null!;
    public IEnumerable<IFSMNode> GetAllTargets() => _transitions.Values;
    public IEnumerator<IFSMNode> GetEnumerator() => Enumerable.Empty<IFSMNode>().GetEnumerator();
    public IEnumerable<FSMTransition> GetFSMTransitions() => Enumerable.Empty<FSMTransition>();
    public FSMTransition GetFSMTransition(string inputNodeName) => null!;

    // ---------- IFSMNode ----------
    public FSMExecutionControl Control { get; } = new();
    public FSMEvent? TriggerEvent { get; set; }
    public IExecuterContext ExecuterContext { get; set; } = null!;
    public void InitBeforeStart() { }
    public void SetBranchEvent(int index, FSMEvent @event) { }
    public void DeleteBranch(int index) { }
    public Task CreateNewAsync() => Task.CompletedTask;
    public event Action<IFSMNode>? ContinueWith;
    public Task<bool> RunAsync() => Task.FromResult(true);
    public Task WaitCurrentTask => Task.CompletedTask;
    public event Action? RaisePause;
    void IFSMEngineInjectable.SetEngine(FSMEngine e) { ReceivedEngine = e; }

    /// <summary>接线后由引擎注入的引用（测试可观察）。</summary>
    public FSMEngine? ReceivedEngine { get; private set; }

    // ---------- IDisposable ----------
    public void Dispose() { }
}
