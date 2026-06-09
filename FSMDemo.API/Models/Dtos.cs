namespace FSMDemo.API.Services;

public enum ExecutionState
{
    Idle,
    Running,
    Paused,
    Stopping,
    Finished
}

public class EngineStatus
{
    public List<string> NodeNames { get; set; } = [];
    public List<string> EventNames { get; set; } = [];
    public int ConnectionCount { get; set; }
}

public class NodeInfo
{
    public string Name { get; set; } = "";
    public string ClassType { get; set; } = "";
    public string Color { get; set; } = "";
    public double PosX { get; set; }
    public double PosY { get; set; }
    public string FlowID { get; set; } = "";
    public string? Discription { get; set; }
    public List<NodeEventDescriptionDto> EventDescriptions { get; set; } = [];
    public List<GroupDefDto>? GroupDefs { get; set; }
}

public class NodeEventDescriptionDto
{
    public int Index { get; set; }
    public string Description { get; set; } = "";
}

public class GroupDefDto
{
    public string StartNode { get; set; } = "";
    public string EndEvent { get; set; } = "";
}

public class ConnectionDto
{
    public string FromNodeName { get; set; } = "";
    public string ToNodeName { get; set; } = "";
    public string EventName { get; set; } = "";
}

public class CreateNodeRequest
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public double PosX { get; set; }
    public double PosY { get; set; }
    public string Color { get; set; } = "";
}

public class CreateConnectionRequest
{
    public string FromNodeName { get; set; } = "";
    public string ToNodeName { get; set; } = "";
    public string EventName { get; set; } = "NextEvent";
}

public class ExecutionStatus
{
    public ExecutionState State { get; set; }
    public string? CurrentNodeName { get; set; }
}

public class ModuleInstanceDto
{
    public string InstanceName { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public double PosX { get; set; }
    public double PosY { get; set; }
    public string Color { get; set; } = "white";
    public string FlowID { get; set; } = "";
    public List<string> InternalNodeNames { get; set; } = [];
    public List<string> InputNodeNames { get; set; } = [];
    public List<string> TerminalNodeNames { get; set; } = [];
    public Dictionary<string, string> OutputEventMap { get; set; } = new();
    public Dictionary<string, string> OutputSourceNodes { get; set; } = new();
}

public class CreateModuleInstanceRequest
{
    public string ModuleName { get; set; } = "";
    public string InstanceName { get; set; } = "";
    public double PosX { get; set; }
    public double PosY { get; set; }
}

public class ModuleDefDto
{
    public string ModuleName { get; set; } = "";
    public List<string> Inputs { get; set; } = [];
    public List<string> Outputs { get; set; } = [];
    public List<string> Terminals { get; set; } = [];
}
