namespace PTfinder.API.DTO.ClientAuth
{
    public class GoogleClientLoginResponse
    {
        public string Token { get; set; } = null!;
        public ClientAuthUserDto Client { get; set; } = null!;
    }

    public class ClientAuthUserDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? PictureUrl { get; set; }
    }
}
