using System.Security.Claims;

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
}
