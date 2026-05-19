using System.Net;
using System.Text.Json;
using SaqueroGateway.Api.Models;

namespace SaqueroGateway.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items["X-Correlation-Id"]?.ToString();
            _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);
            await WriteErrorResponse(context, ex, correlationId);
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, Exception ex, string? correlationId)
    {
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/json";

        var error = new ErrorResponse(
            Code: "INTERNAL_ERROR",
            Message: "An unexpected error occurred.",
            Detail: ex.Message,
            CorrelationId: correlationId
        );

        await context.Response.WriteAsync(JsonSerializer.Serialize(error,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
