namespace PTfinder.API.DATA.DTO;

public sealed class RequestVerificationDto
{
    public string Email { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 30;
}

public sealed class VerifyEmailDto
{
    public string Token { get; set; } = string.Empty;
}

