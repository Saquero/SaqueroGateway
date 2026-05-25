using System.Diagnostics;
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

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(_healthUrl, cancellationToken);
            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = sw.ElapsedMilliseconds,
                ["statusCode"] = (int)response.StatusCode
            };

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"{_serviceName} is reachable. Latency: {sw.ElapsedMilliseconds}ms", data)
                : HealthCheckResult.Degraded($"{_serviceName} returned {response.StatusCode}. Latency: {sw.ElapsedMilliseconds}ms", null, data);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = sw.ElapsedMilliseconds
            };
            return HealthCheckResult.Unhealthy($"{_serviceName} is unreachable after {sw.ElapsedMilliseconds}ms.", ex, data);
        }
    }
}