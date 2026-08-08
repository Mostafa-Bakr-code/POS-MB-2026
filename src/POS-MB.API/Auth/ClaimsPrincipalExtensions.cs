using System.Security.Claims;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Auth;

public static class ClaimsPrincipalExtensions
{
    public static bool HasPermission(this ClaimsPrincipal user, Permission permission)
    {
        var claim = user.FindFirst("permissions")?.Value;
        return claim is not null
            && int.TryParse(claim, out var value)
            && ((Permission)value & permission) != 0;
    }

    // The single source of truth for "who is making this request" - every
    // ownership/attribution check (order creation, log sessions, item price
    // audit trail, logout) needs this same value, so it lives here once instead
    // of each caller re-parsing the claim independently.
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    public static string GetUserName(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? "unknown";

    // Both staff and student tokens always carry an explicit "accountType"
    // claim (see JwtTokenService) - missing/malformed defaults to false
    // (not a student) rather than guessing, the safe direction for an
    // authorization check.
    public static bool IsStudent(this ClaimsPrincipal user) =>
        user.FindFirst("accountType")?.Value == nameof(AccountType.Student);
}
