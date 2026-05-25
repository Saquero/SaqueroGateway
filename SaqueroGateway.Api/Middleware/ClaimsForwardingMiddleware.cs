using System.Security.Claims;

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

            var plan = context.User.FindFirst("plan")?.Value
                    ?? context.User.FindFirst("subscription")?.Value
                    ?? "free";

            var tenantId = context.User.FindFirst("tenant_id")?.Value
                        ?? context.User.FindFirst("tid")?.Value;

            if (userId is not null)
                context.Request.Headers["X-User-Id"] = userId;

            if (userEmail is not null)
                context.Request.Headers["X-User-Email"] = userEmail;

            if (userRole is not null)
                context.Request.Headers["X-User-Role"] = userRole;

            context.Request.Headers["X-User-Plan"] = plan;

            if (tenantId is not null)
                context.Request.Headers["X-Tenant-Id"] = tenantId;

            var correlationId = context.Items["X-Correlation-Id"]?.ToString();
            if (correlationId is not null)
                context.Request.Headers["X-Correlation-Id"] = correlationId;

            var clientIp = context.Connection.RemoteIpAddress?.ToString();
            if (clientIp is not null)
                context.Request.Headers["X-Forwarded-For"] = clientIp;
        }

        await _next(context);
    }
}