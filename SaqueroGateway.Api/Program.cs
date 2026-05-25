using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using SaqueroGateway.Api.Configuration;
using SaqueroGateway.Api.HealthChecks;
using SaqueroGateway.Api.Middleware;
using SaqueroGateway.Api.RateLimiting;
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

    var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
        ?? throw new InvalidOperationException("JwtSettings section is missing from configuration.");

    if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
        throw new InvalidOperationException("JwtSettings:SecretKey is required and cannot be empty.");

    if (string.IsNullOrWhiteSpace(jwtSettings.Issuer))
        throw new InvalidOperationException("JwtSettings:Issuer is required and cannot be empty.");

    if (string.IsNullOrWhiteSpace(jwtSettings.Audience))
        throw new InvalidOperationException("JwtSettings:Audience is required and cannot be empty.");

    builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
    });

    builder.Services.AddRateLimiter(TenantRateLimitingPolicy.Configure);

    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

    // Named HttpClient con resilience pipeline para health checks
    builder.Services.AddHttpClient("health-checks", client =>
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

    builder.Services.AddHealthChecks()
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

    var app = builder.Build();

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