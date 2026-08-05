using Microsoft.AspNetCore.Diagnostics;

namespace POS_MB.API;

// Catches any exception that escapes a controller action anywhere in the API, so
// a bug (or an input nobody thought to validate - see the order-comment-length
// crash this was written in response to) turns into a clean, readable response
// instead of a raw, unhandled crash.
//
// ArgumentException is thrown deliberately throughout the Business layer for bad
// input (e.g. clsOrderBusiness rejecting an empty cart) with a message that's
// already safe to show a client as-is. Anything else is unexpected and might
// contain internal details (SQL, stack traces) that should never reach a
// client - those get logged in full server-side and only a generic message goes
// out over the wire.
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, message) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);

        return true;
    }
}
