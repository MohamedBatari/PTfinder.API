using PTfinder.API.DATA.Modules;
using System.ComponentModel.DataAnnotations;

namespace PTfinder.API.Models
{
    public class ClientContactView
    {
        public long Id { get; set; }

        public int ClientId { get; set; }
        public Client Client { get; set; } = null!;

        public int CoachId { get; set; }
        public Coach Coach { get; set; } = null!;

        [Required, MaxLength(50)]
        public string ActionType { get; set; } = null!;
        // unlock_whatsapp, click_whatsapp, unlock_email, click_email, unlock_phone, click_phone

        public DateTime CreatedAtUtc { get; set; }

        [MaxLength(100)]
        public string? IpAddress { get; set; }

        [MaxLength(1000)]
        public string? UserAgent { get; set; }

        [MaxLength(1000)]
        public string? Referrer { get; set; }

        [MaxLength(100)]
        public string? ClientTimeZone { get; set; }
    }
}
