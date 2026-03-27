namespace PTfinder.API.DTO.ClientContact
{
    public class UnlockContactRequest
    {
        public string ActionType { get; set; } = null!;
        public string? ClientTimeZone { get; set; }
    }
}