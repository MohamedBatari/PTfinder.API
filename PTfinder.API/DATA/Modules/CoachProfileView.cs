using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Models
{
    public class CoachProfileView
    {
        public int Id { get; set; }

        public int CoachId { get; set; }
        public Coach? Coach { get; set; }

        public int? ClientId { get; set; }
        public Client? Client { get; set; }

        public string? SessionId { get; set; }
        public string? ViewSource { get; set; } // coach_page
        public string? Referrer { get; set; }
        public string? ClientTimeZone { get; set; }

        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}