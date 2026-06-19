using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using FSMDemo.API.Hubs;
using FSMDemo.API.Services;

namespace FSMDemo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EngineController : ControllerBase
{
    private readonly EngineManager _engineManager;
    private readonly IHubContext<ExecutionHub> _hubContext;

    public EngineController(EngineManager engineManager, IHubContext<ExecutionHub> hubContext)
    {
        _engineManager = engineManager;
        _hubContext = hubContext;
    }

    [HttpGet]
    public ActionResult<EngineStatus> GetStatus()
    {
        return Ok(_engineManager.GetStatus());
    }

    [HttpGet("export")]
    public ActionResult<string> Export()
    {
        return Ok(_engineManager.Export());
    }

    [HttpGet("modules")]
    public ActionResult<List<ModuleInstanceDto>> GetModuleInstances()
    {
        return Ok(_engineManager.GetModuleInstances());
    }

    [HttpGet("module-definitions")]
    public ActionResult<List<ModuleDefDto>> GetModuleDefinitions()
    {
        return Ok(_engineManager.GetModuleDefinitions());
    }

    [HttpPost("import")]
    public async Task<ActionResult<EngineStatus>> Import([FromBody] ImportRequest request)
    {
        try
        {
            _engineManager.Import(request.Script);
            await _hubContext.Clients.Group("Execution").SendAsync("EngineImported");
            return Ok(_engineManager.GetStatus());
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class ImportRequest
{
    public string Script { get; set; } = "";
}
