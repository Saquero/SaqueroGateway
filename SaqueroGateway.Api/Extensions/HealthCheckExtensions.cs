using Microsoft.Extensions.Diagnostics.HealthChecks;
using SaqueroGateway.Api.HealthChecks;

namespace SaqueroGateway.Api.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddGatewayHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "saquero-cloud",
                sp => new DownstreamHealthCheck("SaqueroCloud", "http://localhost:5000/health", sp.GetRequiredService<IHttpClientFactory>()),
                HealthStatus.Degraded, new[] { "downstream" }))
            .Add(new HealthCheckRegistration(
                "saquero-orders",
                sp => new DownstreamHealthCheck("SaqueroOrderCore", "http://localhost:8080/actuator/health", sp.GetRequiredService<IHttpClientFactory>()),
                HealthStatus.Degraded, new[] { "downstream" }))
            .Add(new HealthCheckRegistration(
                "saquero-jobs",
                sp => new DownstreamHealthCheck("SaqueroJobs", "http://localhost:5200/health", sp.GetRequiredService<IHttpClientFactory>()),
                HealthStatus.Degraded, new[] { "downstream" }));

        return services;
    }
}