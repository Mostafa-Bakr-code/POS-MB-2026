using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController(clsSettingsBusiness settingsBusiness) : ControllerBase
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
