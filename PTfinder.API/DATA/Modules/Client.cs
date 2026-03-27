using System.ComponentModel.DataAnnotations;

namespace PTfinder.API.Models
{
    public class Client
    {
        public int Id { get; set; }

        [Required, MaxLength(128)]
        public string GoogleSub { get; set; } = null!;

        [Required, MaxLength(256)]
        public string Email { get; set; } = null!;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = null!;

        [MaxLength(1000)]
        public string? PictureUrl { get; set; }

        public bool EmailVerified { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastLoginAtUtc { get; set; }

        // Terms & Privacy tracking
        public bool TermsAccepted { get; set; }
        [MaxLength(50)]
        public string? TermsVersion { get; set; }
        public DateTime? TermsAcceptedAtUtc { get; set; }

        public bool PrivacyAccepted { get; set; }
        [MaxLength(50)]
        public string? PrivacyVersion { get; set; }
        public DateTime? PrivacyAcceptedAtUtc { get; set; }

        // Tracking
        [MaxLength(100)]
        public string? LastIpAddress { get; set; }

        [MaxLength(1000)]
        public string? LastUserAgent { get; set; }

        [MaxLength(100)]
        public string? ClientTimeZone { get; set; }

        public ICollection<ClientContactView> ContactViews { get; set; } = new List<ClientContactView>();
    }
}