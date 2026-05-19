using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SaqueroGateway.Api.HealthChecks;

public class DownstreamHealthCheck : IHealthCheck
{
    private readonly string _serviceName;
    private readonly string _healthUrl;

    public DownstreamHealthCheck(string serviceName, string healthUrl)
    {
        _serviceName = serviceName;
        _healthUrl = healthUrl;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(_healthUrl, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"{_serviceName} is reachable.")
                : HealthCheckResult.Degraded($"{_serviceName} returned {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{_serviceName} is unreachable.", ex);
        }
    }
}
