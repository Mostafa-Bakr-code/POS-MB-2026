using System.Security.Claims;
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
    // everyone's logs" (that's GetAll above). The user id comes from the JWT,
    // never the request body - a client-supplied id would let any authenticated
    // user forge a shift-start row for someone else.
    [HttpPost("start")]
    public async Task<IActionResult> StartSession()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var logId = await logsBusiness.StartSessionAsync(userId);
        return Ok(new { LogId = logId });
    }

    [HttpPost("{id:int}/end")]
    public async Task<IActionResult> EndSession(int id)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var ended = await logsBusiness.EndSessionAsync(id, userId);
        return ended ? NoContent() : NotFound();
    }
}
