using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.Options;
using PTfinder.API.Settings;
using AppBillingService = PTfinder.API.Services.BillingService;

using PTfinder.API.Services;
using Stripe;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/stripe/webhook")]
    public class StripeWebhookController : ControllerBase
    {
        private readonly AppBillingService _billing;
        private readonly StripeSettings _cfg;
        private readonly bool _mock;

        public StripeWebhookController(AppBillingService billing, IOptions<StripeSettings> cfg, IConfiguration config)
        {
            _billing = billing;
            _cfg = cfg.Value;

            // Mock mode if explicitly enabled OR no webhook secret configured
            _mock = string.Equals(config["Billing:Mock"], "true", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(_cfg.WebhookSecret);
        }

        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            // In mock mode (or no signature header), skip Stripe verification and just 200 OK.
            // This prevents NullReferenceException when testing manually.
            if (_mock)
            {
                // Optionally: you could parse a minimal mock payload here if you want to call
                // _billing.SyncStripeSubscriptionAsync(...) — but not required for now.
                return Ok(new { mock = true });
            }

            var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();
            if (string.IsNullOrEmpty(signatureHeader))
            {
                // No signature header → don’t try to validate, just ACK to avoid library throwing.
                return Ok(new { ignored = true, reason = "Missing Stripe-Signature header" });
            }

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    signatureHeader,
                    _cfg.WebhookSecret
                );
            }
            catch (Exception ex)
            {
                // Invalid signature or event payload → return 400 so Stripe retries
                return BadRequest(new { error = "Invalid signature", ex.Message });
            }

            switch (stripeEvent.Type)
            {
                case "customer.subscription.created":
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                    {
                        var sub = stripeEvent.Data.Object as global::Stripe.Subscription;
                        if (sub != null) await _billing.SyncStripeSubscriptionAsync(sub);
                        break;
                    }
                case "invoice.paid":
                case "invoice.payment_failed":
                    break;
            }

            return Ok();
        }

        // -------- OPTIONAL: mock endpoint to simulate a subscription (dev only) --------
        // Call this in mock mode to mark a coach or partner active like a webhook would.
        // POST /api/stripe/webhook/mock-activate
        // { "kind":"freelancer", "coachId":7, "tier":"premium", "interval":"month" }
        // or { "kind":"partner", "partnerId":1, "plan":"small", "interval":"month" }
        public class MockActivateDto
        {
            public string kind { get; set; } // "freelancer" | "partner"
            public int? coachId { get; set; }
            public int? partnerId { get; set; }
            public string? tier { get; set; }      // basic|standard|premium
            public string? interval { get; set; }  // month|year
            public string? plan { get; set; }      // small|medium|large
        }

        [HttpPost("mock-activate")]
        public async Task<IActionResult> MockActivate([FromBody] MockActivateDto dto)
        {
            if (!_mock) return Forbid();

            // Build a minimal Stripe.Subscription-like object to reuse your sync logic
            var sub = new global::Stripe.Subscription
            {
                Id = "sub_mock_" + Guid.NewGuid().ToString("N"),
                Status = "active",
                Metadata = new Dictionary<string, string>()
            };

            if (string.Equals(dto.kind, "freelancer", StringComparison.OrdinalIgnoreCase) && dto.coachId.HasValue)
            {
                sub.Metadata["kind"] = "freelancer";
                sub.Metadata["coachId"] = dto.coachId.Value.ToString();
                if (!string.IsNullOrWhiteSpace(dto.tier)) sub.Metadata["tier"] = dto.tier!;
                if (!string.IsNullOrWhiteSpace(dto.interval)) sub.Metadata["interval"] = dto.interval!;
            }
            else if (string.Equals(dto.kind, "partner", StringComparison.OrdinalIgnoreCase) && dto.partnerId.HasValue)
            {
                sub.Metadata["kind"] = "partner";
                sub.Metadata["partnerId"] = dto.partnerId.Value.ToString();
                if (!string.IsNullOrWhiteSpace(dto.plan)) sub.Metadata["plan"] = dto.plan!;
                if (!string.IsNullOrWhiteSpace(dto.interval)) sub.Metadata["interval"] = dto.interval!;
                // You don’t need items/price here; your sync uses plan→seats map only when real priceId exists
            }
            else
            {
                return BadRequest(new { error = "Invalid mock payload" });
            }

            // Set a fake period end ~30 days from now
            // Different SDKs expose CurrentPeriodEnd differently; set both safest fields via reflection is complex.
            // Your Sync uses GetCurrentPeriodEndUtc, which will return null if property not present — OK for mock.
            await _billing.SyncStripeSubscriptionAsync(sub);
            return Ok(new { ok = true });
        }
    }
}
