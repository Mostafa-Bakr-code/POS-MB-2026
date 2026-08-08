using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Auth;

public class JwtTokenService(IConfiguration configuration)
{
    public string GenerateToken(User user) => GenerateToken(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim("accountType", nameof(AccountType.Staff)),
            // Raw permissions bitmask, not yet enforced server-side - that's Step 4
            // (Authorization/Roles). Carried in the token now so it's available once
            // that step reads it, without needing a second round-trip to fetch it.
            new Claim("permissions", user.Permissions.ToString())
        ]);

    // Students have no Permissions bitmask - they're never staff, so there's no
    // "permissions" claim at all. RequirePermission-gated endpoints already
    // reject that as "false" (no claim found), so a student token can never
    // reach a staff-only endpoint even without an explicit check - the
    // "accountType" claim exists for clarity/audit logging and for gating
    // student-only endpoints going the other direction.
    public string GenerateToken(Student student) => GenerateToken(
        [
            new Claim(ClaimTypes.NameIdentifier, student.StudentId.ToString()),
            new Claim(ClaimTypes.Name, student.Email),
            new Claim("accountType", nameof(AccountType.Student))
        ]);

    private string GenerateToken(Claim[] claims)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
        var expiryMinutes = configuration.GetValue<int?>("Jwt:ExpiryMinutes")
            ?? throw new InvalidOperationException("Jwt:ExpiryMinutes is not configured.");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
