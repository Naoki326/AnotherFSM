using Microsoft.AspNetCore.Mvc;
using FSMDemo.API.Services;

namespace FSMDemo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConnectionsController : ControllerBase
{
    private readonly EngineManager _engineManager;

    public ConnectionsController(EngineManager engineManager)
    {
        _engineManager = engineManager;
    }

    [HttpGet]
    public ActionResult<List<ConnectionDto>> GetAll()
    {
        return Ok(_engineManager.GetConnections());
    }

    [HttpPost]
    public ActionResult Create([FromBody] CreateConnectionRequest request)
    {
        var (success, error) = _engineManager.CreateConnection(request.FromNodeName, request.ToNodeName, request.EventName);
        if (success) return Ok();
        return BadRequest(new { error });
    }

    [HttpDelete]
    public ActionResult Delete([FromBody] DeleteConnectionRequest request)
    {
        if (_engineManager.DeleteConnection(request.FromNodeName, request.ToNodeName, request.EventName))
            return Ok();
        return BadRequest(new { error = "Failed to delete connection" });
    }

    [HttpPut("rename")]
    public ActionResult Rename([FromBody] RenameConnectionRequest request)
    {
        if (_engineManager.RenameConnection(request.FromNodeName, request.ToNodeName, request.NewEventName, request.EventName))
            return Ok();
        return BadRequest(new { error = "Failed to rename connection" });
    }
}

public class DeleteConnectionRequest
{
    public string FromNodeName { get; set; } = "";
    public string ToNodeName { get; set; } = "";
    public string? EventName { get; set; }
}

public class RenameConnectionRequest
{
    public string FromNodeName { get; set; } = "";
    public string ToNodeName { get; set; } = "";
    public string? EventName { get; set; }
    public string NewEventName { get; set; } = "";
}
