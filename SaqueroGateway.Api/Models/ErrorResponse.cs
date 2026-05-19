namespace SaqueroGateway.Api.Models;

public sealed record ErrorResponse(
    string Code,
    string Message,
    string? Detail = null,
    string? CorrelationId = null
);
