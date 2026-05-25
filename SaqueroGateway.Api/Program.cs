using Serilog;
using Serilog.Events;
using SaqueroGateway.Api.Extensions;
using SaqueroGateway.Api.HealthChecks;
using SaqueroGateway.Api.Middleware;
using SaqueroGateway.Api.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting SaqueroGateway...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services
        .AddGatewayAuth(builder.Configuration)
        .AddGatewayRateLimiting()
        .AddGatewayResilience()
        .AddGatewayHealthChecks();

    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

    builder.Services.AddProblemDetails();

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        service = "SaqueroGateway",
        version = "2.0.0",
        timestamp = DateTime.UtcNow
    })).AllowAnonymous();

    app.MapHealthChecks("/health/downstream", new HealthCheckOptions
    {
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = 200,
            [HealthStatus.Degraded] = 200,
            [HealthStatus.Unhealthy] = 200
        },
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var result = new
            {
                status = report.Status.ToString(),
                services = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    latencyMs = e.Value.Data.ContainsKey("latencyMs") ? e.Value.Data["latencyMs"] : null,
                    statusCode = e.Value.Data.ContainsKey("statusCode") ? e.Value.Data["statusCode"] : null
                })
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(result,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }).AllowAnonymous();

    app.MapReverseProxy(proxyPipeline =>
    {
        proxyPipeline.UseMiddleware<ClaimsForwardingMiddleware>();
    }).RequireRateLimiting(TenantRateLimitingPolicy.PolicyName);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SaqueroGateway terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}