namespace PTfinder.API.DATA.Modules;
public class GiftCheckoutRequest
{
    public string? CoachId { get; set; }
    public string? CoachName { get; set; }
    public decimal Amount { get; set; } // AED
    public string? Note { get; set; }   // up to 120 chars
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

public class GiftCheckoutResponse
{
    public string? Url { get; set; }
    public string? Message { get; set; }
}

