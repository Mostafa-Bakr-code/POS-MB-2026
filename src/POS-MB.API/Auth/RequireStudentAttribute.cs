using Microsoft.AspNetCore.Authorization;

namespace POS_MB.API.Auth;

// Same shape as RequirePermissionAttribute, but for "is this caller a student"
// rather than a staff permission bit - staff and students are different kinds
// of accounts (students have no Permissions bitmask at all), so this is a
// separate check rather than trying to force it through the permission system.
public class RequireStudentAttribute : AuthorizeAttribute, IAuthorizationRequirementData
{
    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new StudentAccountRequirement()];
}

public class StudentAccountRequirement : IAuthorizationRequirement;

public class StudentAccountAuthorizationHandler(ILogger<StudentAccountAuthorizationHandler> logger)
    : AuthorizationHandler<StudentAccountRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, StudentAccountRequirement requirement)
    {
        if (context.User.IsStudent())
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogWarning("User {UserName} denied: student-only endpoint accessed by a non-student account",
                context.User.GetUserName());
        }

        return Task.CompletedTask;
    }
}
