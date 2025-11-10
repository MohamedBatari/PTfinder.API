using System.ComponentModel.DataAnnotations;

namespace PTfinder.API.DATA.Modules
{
    public class Partner
    {
        public int Id { get; set; }

        // Basic info
        [Required, MaxLength(160)]
        public string Name { get; set; }
        [MaxLength(300)]
        public string? LogoUrl { get; set; }
        [MaxLength(2000)]
        public string? Description { get; set; }

        [MaxLength(160)]
        public string? Email { get; set; }
        [MaxLength(40)]
        public string? Phone { get; set; }
        [MaxLength(300)]
        public string? Address { get; set; }

        // Plan (Small/Medium/Large/Enterprise) — all include Premium seats
        [Required, MaxLength(80)]
        public string PlanName { get; set; }        // e.g., "Small", "Medium", "Large", "Enterprise"
        public int MaxCoaches { get; set; }         // seats: 10 / 25 / 50 / custom
        public decimal PricePerMonth { get; set; }  // for display/reference
        public decimal PricePerYear { get; set; }

        // Stripe linkage (companies)
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public string? StripePriceId { get; set; }
        public DateTime? CurrentPeriodEndUtc { get; set; }
        public bool IsActive { get; set; } = true;

        // Timestamps
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        // Navigation
        public List<Coach> Coaches { get; set; } = new();
    }
}
