using SaqueroGateway.Api.RateLimiting;

namespace SaqueroGateway.Api.Extensions;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(TenantRateLimitingPolicy.Configure);
        return services;
    }
}