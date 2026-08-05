using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(clsUserBusiness userBusiness, JwtTokenService tokenService) : ControllerBase
{
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
        return CreatedAtAction(nameof(GetById), new { id }, ToResponse(user!));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Users)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        var updated = await userBusiness.UpdateAsync(id, request.UserName, request.Password, request.Permissions);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/deactivate")]
    [RequirePermission(Permission.Users)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var deactivated = await userBusiness.DeactivateAsync(id);
        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/reactivate")]
    [RequirePermission(Permission.Users)]
    public async Task<IActionResult> Reactivate(int id)
    {
        var reactivated = await userBusiness.ReactivateAsync(id);
        return reactivated ? NoContent() : NotFound();
    }

    // The only endpoint reachable without a token - this is what issues one.
    [HttpPost("verify-credentials")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyCredentials([FromBody] VerifyCredentialsRequest request)
    {
        var user = await userBusiness.VerifyCredentialsAsync(request.UserName, request.Password);
        if (user is null) return Unauthorized();

        var token = tokenService.GenerateToken(user);
        return Ok(new LoginResponse(token, ToResponse(user)));
    }

    private static UserResponse ToResponse(User user) =>
        new(user.UserId, user.UserName, user.Permissions, user.IsActive, user.CreatedAt, user.UpdatedAt);
}

public record CreateUserRequest(string UserName, string Password, int Permissions);
public record UpdateUserRequest(string UserName, string? Password, int Permissions);
public record VerifyCredentialsRequest(string UserName, string Password);
public record UserResponse(int UserId, string UserName, int Permissions, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
public record LoginResponse(string Token, UserResponse User);
