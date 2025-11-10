using System;

namespace PTfinder.API.DATA.Modules
{
    public class CoachGift
    {
        public int Id { get; set; }
        public int CoachId { get; set; }                 // FK → Coach.Id (int)
        public Coach? Coach { get; set; }                // optional nav

        public long AmountMinor { get; set; }            // fils (AED * 100)
        public string Currency { get; set; } = "AED";
        public string? Note { get; set; }

        public string StripeSessionId { get; set; } = default!;
        public string? StripePaymentIntentId { get; set; }

        // succeeded | refunded | pending | failed
        public string Status { get; set; } = "succeeded";
        public string? DonorEmail { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}

