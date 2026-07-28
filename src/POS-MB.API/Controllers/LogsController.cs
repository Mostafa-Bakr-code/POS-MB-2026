using Microsoft.AspNetCore.Mvc;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController(clsLogsBusiness logsBusiness) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var logs = await logsBusiness.GetAllAsync(startDate, endDate);
        return Ok(logs);
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartSession([FromBody] StartSessionRequest request)
    {
        var logId = await logsBusiness.StartSessionAsync(request.UserId);
        return Ok(new { LogId = logId });
    }

    [HttpPost("{id:int}/end")]
    public async Task<IActionResult> EndSession(int id)
    {
        var ended = await logsBusiness.EndSessionAsync(id);
        return ended ? NoContent() : NotFound();
    }
}

public record StartSessionRequest(int UserId);
