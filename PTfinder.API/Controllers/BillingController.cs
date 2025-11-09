using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.Modules;
using Stripe.Checkout;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BillingController : ControllerBase
{
    [HttpPost("gift/checkout")]
    public async Task<ActionResult<GiftCheckoutResponse>> CreateGiftCheckout([FromBody] GiftCheckoutRequest req)
    {
        if (req == null)
            return BadRequest(new GiftCheckoutResponse { Message = "Invalid request." });

        if (req.Amount < 5)
            return BadRequest(new GiftCheckoutResponse { Message = "Minimum amount is AED 5." });

        var safeNote = (req.Note ?? string.Empty);
        if (safeNote.Length > 120) safeNote = safeNote[..120];

        // Fallback Success/Cancel URL if not provided by client
        var origin = $"{Request.Scheme}://{Request.Host}";
        var successUrl = string.IsNullOrWhiteSpace(req.SuccessUrl)
            ? $"{origin}/gift-success?ok=1"
            : req.SuccessUrl!;
        var cancelUrl = string.IsNullOrWhiteSpace(req.CancelUrl)
            ? $"{origin}/?gift=cancel"
            : req.CancelUrl!;

        // Optional: build a nice product name
        var productName = $"Gift to {(string.IsNullOrWhiteSpace(req.CoachName) ? "Coach" : req.CoachName)}"
                        + (string.IsNullOrWhiteSpace(req.CoachId) ? "" : $" (#{req.CoachId})");

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            PaymentMethodTypes = new List<string> { "card" },
            AllowPromotionCodes = true,
            BillingAddressCollection = "auto",
            SubmitType = "pay",
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "aed",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = productName,
                            Description = string.IsNullOrWhiteSpace(safeNote) ? null : $"Message: {safeNote}"
                        },
                        UnitAmount = (long)Math.Round(req.Amount * 100m) // AED -> fils
                    },
                    Quantity = 1
                }
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string>
            {
                ["coachId"] = req.CoachId ?? "",
                ["note"] = safeNote
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        return Ok(new GiftCheckoutResponse { Url = session.Url });
    }
}

