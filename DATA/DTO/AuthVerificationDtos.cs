using System.ComponentModel.DataAnnotations;

namespace PTfinder.API.DATA.DTO;

public sealed class RequestVerificationDto
{
    [Required, EmailAddress]               // ✅ ensures it's a proper email format
    public string Email { get; set; } = string.Empty;

    [Range(5, 1440)]                       // ✅ min 5 minutes, max 24h
    public int ExpiresMinutes { get; set; } = 30;
}

public sealed class VerifyEmailDto
{
    [Required]                             // ✅ can't be null/empty
    public string Token { get; set; } = string.Empty;
}
