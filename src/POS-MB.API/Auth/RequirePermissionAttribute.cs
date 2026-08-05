using Microsoft.AspNetCore.Authorization;

namespace POS_MB.API.Auth;

// Declarative server-side permission check, enforcing the same Users.Permissions
// bitmask WinForms already uses to hide/show buttons - that client-side hiding
// was never real protection (a raw request bypasses it entirely, as the
// comment-length bug earlier in this project proved), this is what makes it
// real. Multiple bits ORed together (e.g. Reports | DailySummary) means "any
// one of these is enough", not "all required" - used where two different
// screens legitimately share one endpoint.
public class RequirePermissionAttribute(Permission required) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PermissionRequirement(required)];
}

public class PermissionRequirement(Permission required) : IAuthorizationRequirement
{
    public Permission Required { get; } = required;
}

public class PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasPermission(requirement.Required))
        {
            context.Succeed(requirement);
        }
        else
        {
            // Every policy in this app has exactly one PermissionRequirement, so not
            // succeeding here means the request is definitely about to be denied (403) -
            // this is the single choke point for every permission-denied event across
            // every controller, rather than duplicating a log call into each one.
            logger.LogWarning("User {UserName} denied: missing required permission {RequiredPermission}",
                context.User.Identity?.Name ?? "unknown", requirement.Required);
        }

        return Task.CompletedTask;
    }
}
