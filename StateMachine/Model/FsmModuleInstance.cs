namespace StateMachine;

// 模块实例：表示流程中放置的一个模块实例，关联到某个 FsmModuleInfo 模板
public class FsmModuleInstance
{
    // 实例名称，同一流程中唯一
    public string InstanceName { get; set; } = "";

    // 关联的模块名称，对应 FsmModuleInfo.ModuleName
    public string ModuleName { get; set; } = "";

    // 记录实际展开对象的归属；普通同前缀点号名称不自动成为模块内部成员。
    public HashSet<string> ExpandedNodeNames { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> ExpandedEventNames { get; set; } = new(StringComparer.Ordinal);

    // 新 DSL 的公开接口：公开名称 -> 展平后的真实节点/事件名称。
    public Dictionary<string, string> PublicInputs { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> PublicOutputs { get; set; } = new(StringComparer.Ordinal);

    // 仅供历史模型及独立 Fluent API 使用；新 DSL 不生成出口别名。
    // 输出事件映射：key=模块内部 output 事件名, value=映射到外部的实际事件名
    public Dictionary<string, string> OutputEventMap { get; set; } = new();

    // 实例在画布上的 X 坐标
    public double PosX { get; set; }

    // 实例在画布上的 Y 坐标
    public double PosY { get; set; }

    // 实例的显示颜色，默认白色
    public string Color { get; set; } = "white";

    // 实例唯一流 ID，用于运行时标识
    public string FlowID { get; set; } = Guid.NewGuid().ToString();

    // 编译期填充：外部事件名到内部节点列表的映射
    public Dictionary<string, List<string>> ExternalEventToInternalNodes { get; set; } = new();

    // 编译期填充：模块 input 声明对应的展开后内部入口节点
    public List<string> InputNodeNames { get; set; } = new();

    // 编译期填充：模块 terminal 声明对应的展开后内部终止节点
    public List<string> TerminalNodeNames { get; set; } = new();
}
