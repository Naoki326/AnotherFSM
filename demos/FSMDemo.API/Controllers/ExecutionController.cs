using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using FSMDemo.API.Hubs;
using FSMDemo.API.Services;

namespace FSMDemo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExecutionController : ControllerBase
{
    private readonly EngineManager _engineManager;

    public ExecutionController(EngineManager engineManager)
    {
        _engineManager = engineManager;
    }

    [HttpGet("status")]
    public ActionResult<ExecutionStatus> GetStatus()
    {
        return Ok(_engineManager.GetExecutionStatus());
    }

    [HttpPost("start")]
    public async Task<ActionResult> Start([FromBody] StartExecutionRequest request)
    {
        var result = await _engineManager.StartExecution(request.StartNodeName, request.EndEventName);
        if (result) return Ok();
        return BadRequest(new { error = "Failed to start execution" });
    }

    [HttpPost("pause")]
    public async Task<ActionResult> Pause()
    {
        var result = await _engineManager.PauseExecution();
        if (result) return Ok();
        return BadRequest(new { error = "No executor running" });
    }

    [HttpPost("continue")]
    public ActionResult Continue()
    {
        var result = _engineManager.ContinueExecution();
        if (result) return Ok();
        return BadRequest(new { error = "No executor running" });
    }

    [HttpPost("stop")]
    public async Task<ActionResult> Stop()
    {
        var result = await _engineManager.StopExecution();
        if (result) return Ok();
        return BadRequest(new { error = "No executor running" });
    }
}

public class StartExecutionRequest
{
    public string StartNodeName { get; set; } = "Start";
    public string EndEventName { get; set; } = "EndEvent";
}
