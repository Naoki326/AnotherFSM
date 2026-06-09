namespace StateMachine;

// 模块定义模板：保存一个 FSM module 的完整定义信息，用于 import 导入和编译
public class FsmModuleInfo
{
    // 模块名称，用作唯一标识
    public string ModuleName { get; set; } = "";

    // 模块的输入事件列表
    public List<string> Inputs { get; set; } = new();

    // 模块的输出事件列表
    public List<string> Outputs { get; set; } = new();

    // 模块终止节点列表。外部从模块实例出发的连线会落到这些内部节点上。
    public List<string> Terminals { get; set; } = new();

    // 模块脚本完整正文（module body），包含所有节点和连线定义
    public string ScriptContent { get; set; } = "";

    // import 解析时的相对路径，用于定位模块文件
    public string? DirectoryPath { get; set; }

    // ---- 以下为编译期解析后的模板数据 ----

    // 模板节点：key=节点名, value=节点实例（不含前缀，仅作模板）
    public Dictionary<string, IFSMNode> TemplateNodes { get; set; } = new();

    // 模板事件：key=事件名, value=事件实例
    public Dictionary<string, FSMEvent> TemplateEvents { get; set; } = new();

    // 模板内部连线记录（由 BuildTransitionVisitor 后续处理）
    public List<TemplateTransition> PendingTransitions { get; set; } = new();
}

// 模板连线记录
public class TemplateTransition
{
    public string SourceNode { get; set; } = "";
    public string EventName { get; set; } = "";
    public string TargetNode { get; set; } = "";
}
