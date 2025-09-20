using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PTfinder.API.DATA.Modules
{
    // ====== SUBSCRIPTION ENUMS (freelancers) ======
    public enum SubscriptionTier
    {
        None = 0,     // not subscribed
        Basic = 1,    // AED 49 / month
        Standard = 2, // AED 149 / month
        Premium = 3   // AED 249 / month
    }

    public enum SubscriptionStatus
    {
        Inactive = 0,
        Active = 1,
        PastDue = 2,
        Canceled = 3,
        Expired = 4
    }

    public class Coach
    {
        public int Id { get; set; }

        // ===== YOUR EXISTING FIELDS =====
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }           // (later: store hash)
        public string PhoneNumber { get; set; }
        public string Gender { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }

        public int CountryId { get; set; }
        public Country Country { get; set; }

        public int CityId { get; set; }
        public City City { get; set; }

        public int AreaId { get; set; }
        public Area Area { get; set; }

        public int CategoryId { get; set; }
        public Category Category { get; set; }

        public int SpecialityId { get; set; }
        public Speciality Speciality { get; set; }

        public string ProfileImage { get; set; }

        public bool EmailVerified { get; set; } = false;
        public string? EmailVerificationToken { get; set; }
        public DateTime? EmailVerificationExpiresUtc { get; set; }

        public List<Availability> Availabilities { get; set; }
        public List<Booking> Bookings { get; set; }
        public ICollection<Review> Reviews { get; set; }
        public ICollection<Subscription> Subscriptions { get; set; } // if you already have it
        public ICollection<GalleryMedia> GalleryMedia { get; set; }

        // ===== NEW: Company (Partner) linkage =====
        // Null => freelancer. Not null => company coach (auto gets Premium benefits)
        public int? PartnerId { get; set; }
        public Partner? Partner { get; set; }

        // ===== NEW: Freelancer subscription tracking =====
        // These apply only when PartnerId == null
        public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.None;
        public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Inactive;
        public DateTime? SubscriptionStartedAtUtc { get; set; }
        public DateTime? SubscriptionExpiresAtUtc { get; set; }

        // Stripe linkage for freelancers (Partner coaches will use Partner’s Stripe fields)
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public DateTime? CurrentPeriodEndUtc { get; set; }

        // Convenience flags
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        // Read-only: treat company coaches as Premium automatically
        [NotMapped]
        public SubscriptionTier EffectiveTier =>
            PartnerId.HasValue ? SubscriptionTier.Premium : SubscriptionTier;
    }
}

