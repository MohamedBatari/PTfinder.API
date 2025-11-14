// e.g. PTfinder.API/DATA/Modules/SubscriptionCheckoutRequest.cs
namespace PTfinder.API.DATA.Modules
{
    public class SubscriptionCheckoutRequest
    {
        public int CoachId { get; set; }      // ID from your Coach table
        public string Plan { get; set; }      // "basic" or "pro"
        public bool Yearly { get; set; }      // true = yearly, false = monthly
    }

    public class SubscriptionCheckoutResponse
    {
        public string? Url { get; set; }
        public string? Message { get; set; }
    }
}

