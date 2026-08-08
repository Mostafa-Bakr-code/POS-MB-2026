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
public class StudentsController(
    clsStudentBusiness studentBusiness, JwtTokenService tokenService, clsRefreshTokenBusiness refreshTokenBusiness,
    ILogger<StudentsController> logger) : ControllerBase
{
    // No email verification for v1 (deliberately deferred - see project memory) -
    // an account is usable immediately after signup. Rate-limited under the same
    // "login" policy as staff login/refresh, since this accepts a password and
    // could otherwise be used to brute-force-probe which emails are registered.
    [HttpPost("signup")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> SignUp([FromBody] StudentSignUpRequest request)
    {
        var id = await studentBusiness.SignUpAsync(request.Email, request.Password);
        var student = await studentBusiness.GetByIdAsync(id);

        logger.LogInformation("Student account created for {Email} (StudentId={StudentId})", request.Email, id);

        var token = tokenService.GenerateToken(student!);
        var refreshToken = await refreshTokenBusiness.IssueAsync(id, AccountType.Student);
        return Ok(new StudentLoginResponse(token, refreshToken, ToResponse(student!)));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] StudentLoginRequest request)
    {
        var student = await studentBusiness.VerifyCredentialsAsync(request.Email, request.Password);
        if (student is null)
        {
            logger.LogWarning("Failed student login attempt for email {Email} from {RemoteIp}",
                request.Email, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        logger.LogInformation("Student {Email} logged in from {RemoteIp}", student.Email, HttpContext.Connection.RemoteIpAddress);

        var token = tokenService.GenerateToken(student);
        var refreshToken = await refreshTokenBusiness.IssueAsync(student.StudentId, AccountType.Student);
        return Ok(new StudentLoginResponse(token, refreshToken, ToResponse(student)));
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await refreshTokenBusiness.ValidateAndRotateAsync(request.RefreshToken);
        // Mirrors the same guard in UsersController.RefreshToken - a staff token
        // rotating successfully here would otherwise look up its numeric id in
        // the Students table, where a coincidental id collision could
        // authenticate as a completely different student.
        if (result is null || result.Value.AccountType != AccountType.Student) return Unauthorized();

        var student = await studentBusiness.GetByIdAsync(result.Value.UserId);
        if (student is null || !student.IsActive) return Unauthorized();

        var token = tokenService.GenerateToken(student);
        return Ok(new StudentLoginResponse(token, result.Value.NewToken, ToResponse(student)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        var studentId = User.GetUserId();
        var revoked = await refreshTokenBusiness.RevokeAsync(request.RefreshToken, studentId, AccountType.Student);

        if (!revoked)
        {
            logger.LogWarning("Student {Email} attempted to log out with a refresh token that isn't theirs",
                User.GetUserName());
        }

        return NoContent();
    }

    private static StudentResponse ToResponse(Student student) =>
        new(student.StudentId, student.Email, student.IsActive, student.CreatedAt, student.UpdatedAt);
}

// Email/password length bounds match Students.Email NVARCHAR(256) and the same
// defensive plaintext-password cap used for staff (no DB length limit on the
// plaintext itself, only the resulting hash is stored).
public record StudentSignUpRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(200)] string Password);

public record StudentLoginRequest(
    [Required, StringLength(256)] string Email,
    [Required, StringLength(200)] string Password);

public record StudentResponse(int StudentId, string Email, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
public record StudentLoginResponse(string Token, string RefreshToken, StudentResponse Student);
