namespace PTfinder.API.DATA.DTO.ClientAuth
{
    public class RecentProfileVisitorDto
    {
        public int? ClientId { get; set; }
        public string VisitorLabel { get; set; } = "";
        public string? SessionId { get; set; }
        public DateTime ViewedAtUtc { get; set; }
        public string? ViewSource { get; set; }
        public string? ClientTimeZone { get; set; }

    }
}
