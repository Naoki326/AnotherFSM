namespace StateMachine;

/// <summary>
/// 可被注入 <see cref="FSMEngine"/> 引用的对象（按需实现）。
/// </summary>
/// <remarks>
/// <para>
/// 这是 <see cref="IFSMNode"/> 的一个<strong>可选伴生能力</strong>接口，遵循接口隔离原则：
/// <see cref="IFSMNode"/> 本身是纯净的、外部可实现的公开契约；而"拿到所属引擎的回指"
/// 是一项<strong>按需 opt-in</strong> 的能力——只有声明实现本接口的对象，引擎在构图完成时
/// 才会调用 <see cref="SetEngine"/> 把自身注入进去。
/// </para>
/// <para>
/// 典型地，<see cref="AbstractFSMNode"/>（核心提供的节点基类）会实现本接口，
/// 因此所有继承它的节点（GroupNode、用户自定义节点等）自动获得引擎回指。
/// 如果一个 <see cref="IFSMNode"/> 实现者<strong>不</strong>实现本接口，引擎会跳过它、
/// 不做注入——这是被允许的（优雅降级）。
/// </para>
/// </remarks>
public interface IFSMEngineInjectable
{
    /// <summary>
    /// 由 <see cref="FSMEngine"/> 在构图完成时调用，把自身引用注入给本对象。
    /// 通常不应由用户代码直接调用。
    /// </summary>
    void SetEngine(FSMEngine engine);
}
