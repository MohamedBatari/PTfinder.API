namespace PTfinder.API.DATA.DTO
{
    public class RequestOtpDto
    {
        public string Email { get; set; } = string.Empty;
        public int ExpiresMinutes { get; set; } = 10;
    }
}

