using Microsoft.AspNetCore.Diagnostics;
using POS_MB.DataAccess.Models;

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
        // Carries structured per-item detail alongside the message, so a
        // client (the mobile cart) can auto-remove exactly the offending
        // items instead of requiring the user to find them manually.
        if (exception is ItemsUnavailableException itemsUnavailable)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                error = itemsUnavailable.Message,
                unavailableItems = itemsUnavailable.Items
            }, cancellationToken);
            return true;
        }

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
