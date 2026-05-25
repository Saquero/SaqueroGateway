using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SaqueroGateway.Api.RateLimiting;

public static class TenantRateLimitingPolicy
{
    public const string PolicyName = "tenant-aware";

    private static readonly Dictionary<string, (int PermitLimit, TimeSpan Window)> PlanLimits = new()
    {
        ["free"]    = (30,  TimeSpan.FromMinutes(1)),
        ["premium"] = (200, TimeSpan.FromMinutes(1)),
        ["admin"]   = (500, TimeSpan.FromMinutes(1))
    };

    public static void Configure(RateLimiterOptions options)
    {
        options.AddPolicy(PolicyName, context =>
        {
            var plan = context.User.FindFirst("plan")?.Value
                    ?? context.User.FindFirst("subscription")?.Value
                    ?? "free";

            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? context.User.FindFirst("sub")?.Value
                      ?? context.Connection.RemoteIpAddress?.ToString()
                      ?? "anonymous";

            if (!PlanLimits.TryGetValue(plan.ToLowerInvariant(), out var limits))
                limits = PlanLimits["free"];

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{plan}:{userId}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit,
                    Window = limits.Window,
                    QueueLimit = 0
                });
        });

        options.OnRejected = async (context, cancellationToken) =>
        {
            var plan = context.HttpContext.User.FindFirst("plan")?.Value ?? "free";
            var userId = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? context.HttpContext.User.FindFirst("sub")?.Value
                      ?? "anonymous";

            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<RateLimiterOptions>>();

            logger.LogWarning(
                "Rate limit exceeded. UserId={UserId} Plan={Plan} Path={Path}",
                userId, plan, context.HttpContext.Request.Path);

            context.HttpContext.Response.StatusCode = 429;
            context.HttpContext.Response.ContentType = "application/json";

            var limits = PlanLimits.TryGetValue(plan.ToLowerInvariant(), out var l) ? l : PlanLimits["free"];

            await context.HttpContext.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    code = "RATE_LIMIT_EXCEEDED",
                    message = $"Rate limit exceeded for plan '{plan}'. Limit: {limits.PermitLimit} requests per minute.",
                    plan,
                    retryAfter = "60 seconds"
                }),
                cancellationToken);
        };

        options.RejectionStatusCode = 429;
    }
}