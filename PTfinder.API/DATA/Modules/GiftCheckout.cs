namespace PTfinder.API.DATA.Modules
{
    public class GiftCheckoutRequest
    {
        public string? CoachId { get; set; }      // REQUIRED
        public string? CoachName { get; set; }    // optional (used in product name)
        public decimal Amount { get; set; }       // REQUIRED, >= 5
        public string? Note { get; set; }         // optional, <= 120 chars
        public string? SuccessUrl { get; set; }   // optional
        public string? CancelUrl { get; set; }    // optional
    }

    public class GiftCheckoutResponse
    {
        public string? Url { get; set; }
        public string? Message { get; set; }      // describes validation errors, etc.
    }
}
