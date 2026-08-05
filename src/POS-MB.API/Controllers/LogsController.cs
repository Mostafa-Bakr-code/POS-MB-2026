using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController(clsLogsBusiness logsBusiness) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.Logs)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var logs = await logsBusiness.GetAllAsync(startDate, endDate);
        return Ok(logs);
    }

    // Not gated behind Permission.Logs - every user starts/ends their own shift
    // session at login/logout regardless of what they're otherwise permitted to
    // do. This tracks the currently-authenticated user's own session, not "view
    // everyone's logs" (that's GetAll above).
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
