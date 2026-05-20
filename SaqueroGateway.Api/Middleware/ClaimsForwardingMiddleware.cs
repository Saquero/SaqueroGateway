using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace SaqueroGateway.Api.Middleware;

public class ClaimsForwardingMiddleware
{
    private readonly RequestDelegate _next;

    public ClaimsForwardingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? context.User.FindFirst("sub")?.Value;

            var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value
                         ?? context.User.FindFirst("email")?.Value;

            var userRole = context.User.FindFirst(ClaimTypes.Role)?.Value
                        ?? context.User.FindFirst("role")?.Value;

            if (userId != null)
                context.Request.Headers["X-User-Id"] = userId;

            if (userEmail != null)
                context.Request.Headers["X-User-Email"] = userEmail;

            if (userRole != null)
                context.Request.Headers["X-User-Role"] = userRole;

            var correlationId = context.Items["X-Correlation-Id"]?.ToString();
            if (correlationId != null)
                context.Request.Headers["X-Correlation-Id"] = correlationId;
        }

        await _next(context);
    }
}
