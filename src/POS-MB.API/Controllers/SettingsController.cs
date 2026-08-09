using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController(clsSettingsBusiness settingsBusiness, clsOrderBusiness orderBusiness) : ControllerBase
{
    // Open to any authenticated user - FormLogIn reads TimeZoneOffsetHours right
    // after login regardless of the logging-in user's permissions.
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var settings = await settingsBusiness.GetAllAsync();
        return Ok(settings);
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> GetByKey([StringLength(100)] string key)
    {
        var setting = await settingsBusiness.GetByKeyAsync(key);
        return setting is null ? NotFound() : Ok(setting);
    }

    // Its own endpoint, gated by Permission.Orders rather than the generic PUT
    // below (Permission.Settings) - this needs to be flippable by whoever's
    // actually running the Order Status screen (too busy, closing soon, a
    // connectivity worry), not locked behind the separate admin-only Settings
    // screen. Same reasoning as Items getting its own dedicated availability
    // toggle instead of going through the full edit permission.
    [HttpPost("accepting-online-orders")]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> SetAcceptingOnlineOrders([FromBody] bool isAccepting)
    {
        await settingsBusiness.SetAsync(clsOrderBusiness.AcceptingOnlineOrdersSettingKey, isAccepting ? "true" : "false");
        return NoContent();
    }

    // Called every ~15s by the WinForms Order Status screen while it's open -
    // proof that someone is actually watching the queue right now, not just
    // that the API happens to be reachable. See clsOrderBusiness.RecordHeartbeatAsync.
    [HttpPost("heartbeat")]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> Heartbeat()
    {
        await orderBusiness.RecordHeartbeatAsync();
        return NoContent();
    }

    // Combines the manual toggle and the heartbeat-derived offline check into
    // one answer (clsOrderBusiness.GetAcceptingOnlineOrdersStatusAsync) - the
    // mobile menu's banner and CreateOrderAsync's own enforcement both read
    // from this exact same logic, so they can never disagree about why.
    [HttpGet("accepting-online-orders-status")]
    public async Task<IActionResult> GetAcceptingOnlineOrdersStatus()
    {
        var (isAccepting, reason) = await orderBusiness.GetAcceptingOnlineOrdersStatusAsync();
        return Ok(new { isAccepting, reason });
    }

    [HttpPut("{key}")]
    [RequirePermission(Permission.Settings)]
    public async Task<IActionResult> Set([StringLength(100)] string key, [FromBody] SetSettingRequest request)
    {
        await settingsBusiness.SetAsync(key, request.Value);
        var setting = await settingsBusiness.GetByKeyAsync(key);
        return Ok(setting);
    }

    [HttpDelete("{key}")]
    [RequirePermission(Permission.Settings)]
    public async Task<IActionResult> Delete([StringLength(100)] string key)
    {
        var deleted = await settingsBusiness.DeleteAsync(key);
        return deleted ? NoContent() : NotFound();
    }
}

// key length above matches Settings.[Key] NVARCHAR(100). Value has no length
// limit here since the column itself is NVARCHAR(MAX).
public record SetSettingRequest(string? Value);
