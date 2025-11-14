namespace PTfinder.API.Services
{
    public class StripeSettings
    {
        public string? SecretKey { get; set; }
        public string? PublishableKey { get; set; }

        // Subscription price IDs (TEST mode now)
        public string? BasicMonthlyPriceId { get; set; }
        public string? BasicYearlyPriceId { get; set; }
        public string? ProMonthlyPriceId { get; set; }
        public string? ProYearlyPriceId { get; set; }

        public string? SuccessUrl { get; set; }
        public string? CancelUrl { get; set; }

        // (optional) front base for emails, etc.
        public string? FrontendBase { get; set; }
    }
}
