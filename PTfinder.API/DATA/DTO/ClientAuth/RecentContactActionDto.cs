namespace PTfinder.API.DATA.DTO.ClientAuth
{
    public class RecentContactActionDto
    {
        public int? ClientId { get; set; }
        public string VisitorLabel { get; set; } = "";
        public string ActionType { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public string? ClientTimeZone { get; set; }
    }
}
