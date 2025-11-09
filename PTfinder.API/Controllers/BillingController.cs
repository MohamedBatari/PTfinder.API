using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BillingController : ControllerBase
    {
        // POST: /api/Billing/gift/checkout
        [HttpPost("gift/checkout")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult<GiftCheckoutResponse>> CreateGiftCheckout([FromBody] GiftCheckoutRequest? req)
        {
            // If JSON body didn’t bind, tell the client exactly why.
            if (req == null)
                return BadRequest(new GiftCheckoutResponse { Message = "Invalid JSON body. Send application/json with body { coachId, amount, note?, successUrl?, cancelUrl? }." });

            // Normalize & validate
            req.CoachId = (req.CoachId ?? string.Empty).Trim();
            req.CoachName = (req.CoachName ?? string.Empty).Trim();
            req.Note = (req.Note ?? string.Empty).Trim();
            if (req.Note.Length > 120) req.Note = req.Note[..120];

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(req.CoachId)) errors.Add("coachId is required.");
            if (req.Amount < 5) errors.Add("Minimum amount is AED 5.");

            if (errors.Any())
                return BadRequest(new GiftCheckoutResponse { Message = string.Join(" ", errors) });

            // Fallback URLs
            var origin = $"{Request.Scheme}://{Request.Host}";
            var successUrl = string.IsNullOrWhiteSpace(req.SuccessUrl) ? $"{origin}/gift-success?ok=1" : req.SuccessUrl!;
            var cancelUrl = string.IsNullOrWhiteSpace(req.CancelUrl) ? $"{origin}/?gift=cancel" : req.CancelUrl!;

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
                                Description = string.IsNullOrWhiteSpace(req.Note) ? null : $"Message: {req.Note}"
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
                    ["coachId"] = req.CoachId!,
                    ["note"] = req.Note!
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Ok(new GiftCheckoutResponse { Url = session.Url });
        }
    }
}
