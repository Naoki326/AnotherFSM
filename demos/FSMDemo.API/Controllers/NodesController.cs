using Microsoft.AspNetCore.Mvc;
using FSMDemo.API.Services;

namespace FSMDemo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NodesController : ControllerBase
{
    private readonly EngineManager _engineManager;

    public NodesController(EngineManager engineManager)
    {
        _engineManager = engineManager;
    }

    [HttpGet]
    public ActionResult<List<NodeInfo>> GetAll()
    {
        return Ok(_engineManager.GetNodes());
    }

    [HttpGet("{name}")]
    public ActionResult<NodeInfo> Get(string name)
    {
        var node = _engineManager.GetNode(name);
        if (node == null) return NotFound();
        return Ok(node);
    }

    [HttpPost]
    public ActionResult<NodeInfo> Create([FromBody] CreateNodeRequest request)
    {
        try
        {
            var node = _engineManager.CreateNode(request.Type, request.Name, request.PosX, request.PosY, request.Color);
            return Ok(node);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{name}")]
    public ActionResult Delete(string name)
    {
        if (_engineManager.DeleteNode(name))
            return Ok();
        return NotFound();
    }

    [HttpPut("{name}/rename")]
    public ActionResult Rename(string name, [FromBody] RenameRequest request)
    {
        if (_engineManager.RenameNode(name, request.NewName))
            return Ok();
        return BadRequest(new { error = "Rename failed" });
    }

    [HttpPut("{name}/position")]
    public ActionResult UpdatePosition(string name, [FromBody] PositionRequest request)
    {
        if (_engineManager.UpdateNodePosition(name, request.PosX, request.PosY))
            return Ok();
        return NotFound(new { error = "Node not found" });
    }

    [HttpPut("{name}/events/{index}")]
    public ActionResult UpdateEvent(string name, int index, [FromBody] UpdateEventRequest request)
    {
        if (_engineManager.UpdateNodeEvent(name, index, request.NewEventName))
            return Ok();
        return NotFound(new { error = "Node not found" });
    }

    [HttpPut("{name}/groupdefs")]
    public ActionResult UpdateGroupDefs(string name, [FromBody] List<GroupDefDto> groupDefs)
    {
        if (_engineManager.UpdateGroupDefs(name, groupDefs))
            return Ok();
        return NotFound(new { error = "Node not found or not a group/parallel node" });
    }

    [HttpPut("{name}/module-terminal")]
    public ActionResult UpdateModuleTerminal(string name, [FromBody] UpdateModuleTerminalRequest request)
    {
        if (_engineManager.UpdateModuleTerminal(name, request.IsTerminal))
            return Ok();
        return NotFound(new { error = "Module internal node not found" });
    }

    [HttpPost("module-instance")]
    public ActionResult<ModuleInstanceDto> CreateModuleInstance([FromBody] CreateModuleInstanceRequest request)
    {
        try
        {
            var instance = _engineManager.AddModuleInstance(
                request.ModuleName, request.InstanceName, request.PosX, request.PosY);
            return Ok(instance);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("module-instance/{instanceName}")]
    public ActionResult DeleteModuleInstance(string instanceName)
    {
        if (_engineManager.DeleteModuleInstance(instanceName))
            return Ok();
        return NotFound(new { error = "Module instance not found" });
    }

    [HttpPut("module-instance/{instanceName}/position")]
    public ActionResult UpdateModuleInstancePosition(string instanceName, [FromBody] PositionRequest request)
    {
        if (_engineManager.UpdateModuleInstancePosition(instanceName, request.PosX, request.PosY))
            return Ok();
        return NotFound(new { error = "Module instance not found" });
    }
}

public class PositionRequest
{
    public double PosX { get; set; }
    public double PosY { get; set; }
}

public class RenameRequest
{
    public string NewName { get; set; } = "";
}

public class UpdateEventRequest
{
    public string NewEventName { get; set; } = "";
}

public class UpdateModuleTerminalRequest
{
    public bool IsTerminal { get; set; }
}
