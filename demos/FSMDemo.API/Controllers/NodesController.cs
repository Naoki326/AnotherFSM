using Microsoft.AspNetCore.Mvc;
using FSMDemo.Runtime;

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
            if (request.AdditionalProperties?.Keys.Any(key => string.Equals(key, "groupDefs", StringComparison.OrdinalIgnoreCase)) == true)
                throw new NotSupportedException(StateMachine.RemovedComposition.Diagnostic);
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
        return BadRequest(new { error = "Node not found or module output renaming is disabled; edit the module template Branch instead" });
    }

    [HttpPut("{name}/groupdefs")]
    // Rejection-only tombstone: never accepts or applies legacy definitions.
    public ActionResult RejectRuntimeComposition(string name, [FromBody] System.Text.Json.JsonElement payload)
        => BadRequest(new { error = StateMachine.RemovedComposition.Diagnostic });

    [HttpPut("{name}/module-terminal")]
    public ActionResult UpdateModuleTerminal(string name, [FromBody] UpdateModuleTerminalRequest request)
    {
        return BadRequest(new { error = "Module terminal configuration is disabled; connect a public output instead" });
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
