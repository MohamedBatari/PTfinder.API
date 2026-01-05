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

        // ===== CORE FIELDS =====
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }           // (later: store hash)
        public string PhoneNumber { get; set; }
        public string Gender { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }

        // Stripe connected account (for payouts / gifts)
        public string? StripeAccountId { get; set; }

        // NEW: Stripe status flags (filled from Stripe Account object)
        public bool StripeChargesEnabled { get; set; }
        public bool StripePayoutsEnabled { get; set; }
        public bool StripeDetailsSubmitted { get; set; }

        // ===== LOCATION / CATEGORY =====
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

        // ===== EMAIL VERIFICATION =====
        public bool EmailVerified { get; set; } = false;
        public string? EmailVerificationToken { get; set; }
        public DateTime? EmailVerificationExpiresUtc { get; set; }

        // ===== NAV PROPERTIES =====
        public List<Availability> Availabilities { get; set; }
        public List<Booking> Bookings { get; set; }
        public ICollection<Review> Reviews { get; set; }
        public ICollection<GalleryMedia> GalleryMedia { get; set; }

        // ===== Company (Partner) linkage =====
        // Null => freelancer. Not null => company coach (auto gets Premium benefits)
        public int? PartnerId { get; set; }
        public Partner? Partner { get; set; }

        // ===== Freelancer subscription tracking =====
        // These apply only when PartnerId == null
        public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.None;
        public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Inactive;
        public DateTime? SubscriptionStartedAtUtc { get; set; }
        public DateTime? SubscriptionExpiresAtUtc { get; set; }

        // Stripe linkage for freelancers (Partner coaches will use Partner’s Stripe fields)
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public DateTime? CurrentPeriodEndUtc { get; set; }

        // ===== Convenience flags =====
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        public string? TermsVersionAccepted { get; set; }
        public DateTime? TermsAcceptedAtUtc { get; set; }
        public string? TermsAcceptedIp { get; set; } // optional
        public string? UserAgent { get; set; }       // optional
        public string? ClientTimeZone { get; set; }  // optional

        public string? PrivacyVersionAccepted { get; set; }
        public DateTime? PrivacyAcceptedAtUtc { get; set; }
        public string? PrivacyAcceptedIp { get; set; }   // optional
        public string? PrivacyLanguage { get; set; }     // "en" or "ar" (optional)


    }
}
