namespace SaqueroGateway.Api.Extensions;

public static class ResilienceExtensions
{
    public static IServiceCollection AddGatewayResilience(this IServiceCollection services)
    {
        services.AddHttpClient("health-checks", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.FromMilliseconds(200);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 3;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(4);
        });

        return services;
    }
}