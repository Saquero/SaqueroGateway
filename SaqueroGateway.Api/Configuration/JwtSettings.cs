using System.ComponentModel.DataAnnotations;

namespace SaqueroGateway.Api.Configuration;

public sealed class JwtSettings
{
    [Required(ErrorMessage = "JwtSettings:SecretKey is required.")]
    [MinLength(32, ErrorMessage = "JwtSettings:SecretKey must be at least 32 characters.")]
    public string SecretKey { get; init; } = string.Empty;

    [Required(ErrorMessage = "JwtSettings:Issuer is required.")]
    public string Issuer { get; init; } = string.Empty;

    [Required(ErrorMessage = "JwtSettings:Audience is required.")]
    public string Audience { get; init; } = string.Empty;
}