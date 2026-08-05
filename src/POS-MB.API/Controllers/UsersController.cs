using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS_MB.API.Auth;
using POS_MB.Business;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(
    clsUserBusiness userBusiness, JwtTokenService tokenService, clsRefreshTokenBusiness refreshTokenBusiness,
    ILogger<UsersController> logger) : ControllerBase
{
    // Who's making this request, for audit log lines below - not the caller's
    // own identity for authorization (that's RequirePermission), just "who did
    // this" for the log entry.
    private string CurrentUserName => User.Identity?.Name ?? "unknown";
    // Open to any authenticated user (not gated behind Permission.Users) -
    // the Logs and Order History screens both need this to resolve user names
    // for display, without either of those permissions implying user management
    // rights. No password hash is ever included in the response.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
    {
        var users = await userBusiness.GetAllAsync(includeInactive);
        return Ok(users.Select(ToResponse));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await userBusiness.GetByIdAsync(id);
        return user is null ? NotFound() : Ok(ToResponse(user));
    }

    [HttpPost]
    [RequirePermission(Permission.Users)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var id = await userBusiness.CreateAsync(request.UserName, request.Password, request.Permissions);
        var user = await userBusiness.GetByIdAsync(id);
        logger.LogInformation("User {ActingUser} created user {TargetUser} (UserId={UserId}, Permissions={Permissions})",
            CurrentUserName, request.UserName, id, request.Permissions);
        return CreatedAtAction(nameof(GetById), new { id }, ToResponse(user!));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Users)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        var updated = await userBusiness.UpdateAsync(id, request.UserName, request.Password, request.Permissions);
        if (updated)
        {
            logger.LogInformation("User {ActingUser} updated user {TargetUser} (UserId={UserId}, Permissions={Permissions}, PasswordChanged={PasswordChanged})",
                CurrentUserName, request.UserName, id, request.Permissions, !string.IsNullOrWhiteSpace(request.Password));
        }
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/deactivate")]
    [RequirePermission(Permission.Users)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var deactivated = await userBusiness.DeactivateAsync(id);
        if (deactivated) logger.LogInformation("User {ActingUser} deactivated user UserId={UserId}", CurrentUserName, id);
        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/reactivate")]
    [RequirePermission(Permission.Users)]
    public async Task<IActionResult> Reactivate(int id)
    {
        var reactivated = await userBusiness.ReactivateAsync(id);
        if (reactivated) logger.LogInformation("User {ActingUser} reactivated user UserId={UserId}", CurrentUserName, id);
        return reactivated ? NoContent() : NotFound();
    }

    // The only endpoint reachable without a token - this is what issues one.
    [HttpPost("verify-credentials")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> VerifyCredentials([FromBody] VerifyCredentialsRequest request)
    {
        var user = await userBusiness.VerifyCredentialsAsync(request.UserName, request.Password);
        if (user is null)
        {
            logger.LogWarning("Failed login attempt for username {UserName} from {RemoteIp}",
                request.UserName, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        logger.LogInformation("User {UserName} logged in from {RemoteIp}", user.UserName, HttpContext.Connection.RemoteIpAddress);

        var token = tokenService.GenerateToken(user);
        var refreshToken = await refreshTokenBusiness.IssueAsync(user.UserId);
        return Ok(new LoginResponse(token, refreshToken, ToResponse(user)));
    }

    // Exchanges a still-valid refresh token for a new access token, without
    // needing the password again - this is what lets a shift stay logged in
    // past the access token's short lifetime. AllowAnonymous because the
    // access token has typically already expired by the time this is called;
    // the refresh token itself is the credential being presented here.
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await refreshTokenBusiness.ValidateAndRotateAsync(request.RefreshToken);
        if (result is null) return Unauthorized();

        var user = await userBusiness.GetByIdAsync(result.Value.UserId);
        if (user is null || !user.IsActive) return Unauthorized();

        var token = tokenService.GenerateToken(user);
        return Ok(new LoginResponse(token, result.Value.NewToken, ToResponse(user)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        await refreshTokenBusiness.RevokeAsync(request.RefreshToken);
        return NoContent();
    }

    private static UserResponse ToResponse(User user) =>
        new(user.UserId, user.UserName, user.Permissions, user.IsActive, user.CreatedAt, user.UpdatedAt);
}

// UserName length matches Users.UserName NVARCHAR(50). Password has no DB length
// limit (only the resulting hash is stored), but a bound still avoids hashing an
// arbitrarily huge input.
public record CreateUserRequest(
    [Required, StringLength(50)] string UserName,
    [Required, StringLength(200)] string Password,
    int Permissions);

public record UpdateUserRequest(
    [Required, StringLength(50)] string UserName,
    [StringLength(200)] string? Password,
    int Permissions);

public record VerifyCredentialsRequest(
    [Required, StringLength(50)] string UserName,
    [Required, StringLength(200)] string Password);

public record RefreshTokenRequest([Required, StringLength(1000)] string RefreshToken);
public record UserResponse(int UserId, string UserName, int Permissions, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
public record LoginResponse(string Token, string RefreshToken, UserResponse User);
