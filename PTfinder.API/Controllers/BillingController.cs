using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using Stripe;
using Stripe.Checkout;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BillingController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;

        public BillingController(AppDbContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;

            // Prefer Program.cs for this; keep here as a fallback so local calls work.
            if (string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey))
                StripeConfiguration.ApiKey = _cfg["Stripe:SecretKey"];
        }

        private string FrontendBase =>
            _cfg["Stripe:FrontendBase"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";

        private static bool TryCoachId(string? s, out int id) =>
            int.TryParse((s ?? "").Trim(), out id);

        // ---------- Quick probe ----------
        [HttpGet("debug/stripe")]
        public ActionResult<object> StripeDebug()
        {
            var hasKey = !string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey);
            return Ok(new { stripeConfigured = hasKey, frontendBase = FrontendBase });
        }

        // =====================================================================
        // Stripe Connect (AE): Express account with transfers only
        // =====================================================================

        public class ConnectAccountRequest { public string? CoachId { get; set; } }

        // POST /api/Billing/connect/account
        [HttpPost("connect/account")]
        public async Task<ActionResult<object>> CreateOrFetchConnectAccount([FromBody] ConnectAccountRequest body)
        {
            var cid = HttpContext.TraceIdentifier;

            try
            {
                if (body == null)
                    return BadRequest(new { message = "Body required: { coachId }" });

                if (!TryCoachId(body.CoachId, out var coachIdInt))
                    return BadRequest(new { message = "coachId must be an integer" });

                var key = _cfg["Stripe:SecretKey"];
                if (string.IsNullOrWhiteSpace(key))
                    return StatusCode(500, new { message = "Stripe not configured (SecretKey missing)" });

                var coach = await _db.Coaches.AsNoTracking().FirstOrDefaultAsync(c => c.Id == coachIdInt);
                if (coach == null)
                    return NotFound(new { message = "Coach not found" });

                // If already has a Connect account, return it
                if (!string.IsNullOrWhiteSpace(coach.StripeAccountId))
                    return Ok(new { accountId = coach.StripeAccountId });

                // AE-compatible Express account: request ONLY transfers
                var acctSvc = new AccountService();
                var acct = await acctSvc.CreateAsync(new AccountCreateOptions
                {
                    Country = "AE",
                    Type = "express",
                    Email = string.IsNullOrWhiteSpace(coach.Email) ? null : coach.Email,
                    // DefaultCurrency = "aed", // optional; onboarding fills this anyway
                    Capabilities = new AccountCapabilitiesOptions
                    {
                        Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
                    }
                });

                var tracked = await _db.Coaches.FirstAsync(c => c.Id == coachIdInt);
                tracked.StripeAccountId = acct.Id;
                await _db.SaveChangesAsync();

                return Ok(new { accountId = acct.Id });
            }
            catch (StripeException se)
            {
                // Friendly handling for the exact error you’re seeing
                var msg = se.StripeError?.Message ?? se.Message;
                var type = se.StripeError?.Type;
                var param = se.StripeError?.Param;
                var code = se.StripeError?.Code;

                if ((type == "invalid_request_error" || type == "api_error") &&
                    msg?.Contains("signed up for Connect", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return StatusCode(400, new
                    {
                        message = "Stripe Connect is not enabled on your platform in this mode.",
                        hint = "Open Stripe Dashboard → (Test mode ON) → Settings → Connect → Enable Connect (Express). Then retry.",
                        stripeError = new { type, code, param, msg }
                    });
                }

                return StatusCode(502, new
                {
                    message = "Stripe error creating Connect account",
                    type,
                    code,
                    param,
                    error = msg
                });
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { message = "Server error creating Connect account", error = ex.Message, cid });
            }
        }

        // POST /api/Billing/connect/account-link
        [HttpPost("connect/account-link")]
        public async Task<ActionResult<object>> CreateAccountLink([FromBody] ConnectAccountRequest body)
        {
            try
            {
                if (!TryCoachId(body?.CoachId, out var coachIdInt))
                    return BadRequest(new { message = "coachId must be an integer" });

                var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachIdInt);
                if (coach == null || string.IsNullOrWhiteSpace(coach.StripeAccountId))
                    return BadRequest(new { message = "Coach missing Stripe account. Call /connect/account first." });

                var linkSvc = new AccountLinkService();
                var link = await linkSvc.CreateAsync(new AccountLinkCreateOptions
                {
                    Account = coach.StripeAccountId,
                    Type = "account_onboarding",
                    RefreshUrl = $"{FrontendBase}/dashboard/gifts?stripe=refresh",
                    ReturnUrl = $"{FrontendBase}/dashboard/gifts?stripe=done"
                });

                return Ok(new { url = link.Url });
            }
            catch (StripeException se)
            {
                return StatusCode(502, new
                {
                    message = "Stripe error creating AccountLink",
                    type = se.StripeError?.Type,
                    code = se.StripeError?.Code,
                    param = se.StripeError?.Param,
                    error = se.StripeError?.Message ?? se.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Server error", error = ex.Message });
            }
        }

        // GET /api/Billing/connect/status/{coachId}
        [HttpGet("connect/status/{coachId}")]
        public async Task<ActionResult<object>> ConnectStatus([FromRoute] string coachId)
        {
            try
            {
                if (!TryCoachId(coachId, out var coachIdInt))
                    return BadRequest(new { message = "coachId must be an integer" });

                var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachIdInt);
                if (coach == null) return NotFound(new { message = "Coach not found" });

                if (string.IsNullOrWhiteSpace(coach.StripeAccountId))
                    return Ok(new { connected = false });

                var svc = new AccountService();
                var acct = await svc.GetAsync(coach.StripeAccountId);

                return Ok(new
                {
                    connected = true,
                    accountId = acct.Id,
                    chargesEnabled = acct.ChargesEnabled,
                    payoutsEnabled = acct.PayoutsEnabled,
                    requirements = acct.Requirements?.CurrentlyDue
                });
            }
            catch (StripeException se)
            {
                return StatusCode(502, new
                {
                    message = "Stripe error fetching account status",
                    type = se.StripeError?.Type,
                    code = se.StripeError?.Code,
                    param = se.StripeError?.Param,
                    error = se.StripeError?.Message ?? se.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Server error", error = ex.Message });
            }
        }

        // =====================================================================
        // Gifts checkout: destination charges (platform fee + payout to coach)
        // =====================================================================

        [HttpPost("gift/checkout")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult<GiftCheckoutResponse>> CreateGiftCheckout([FromBody] GiftCheckoutRequest? req)
        {
            try
            {
                if (req == null)
                    return BadRequest(new GiftCheckoutResponse { Message = "Invalid JSON body. Expected { coachId, amount, note?, successUrl?, cancelUrl? }" });

                req.CoachId = (req.CoachId ?? string.Empty).Trim();
                req.CoachName = (req.CoachName ?? string.Empty).Trim();
                req.Note = (req.Note ?? string.Empty).Trim();
                if (req.Note.Length > 120) req.Note = req.Note[..120];

                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(req.CoachId)) errors.Add("coachId is required.");
                if (req.Amount < 5) errors.Add("Minimum amount is AED 5.");
                if (errors.Any())
                    return BadRequest(new GiftCheckoutResponse { Message = string.Join(" ", errors) });

                if (!TryCoachId(req.CoachId, out var coachIdInt))
                    return BadRequest(new GiftCheckoutResponse { Message = "coachId must be an integer" });

                var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachIdInt);
                if (coach == null)
                    return NotFound(new GiftCheckoutResponse { Message = "Coach not found." });

                if (string.IsNullOrWhiteSpace(coach.StripeAccountId))
                    return BadRequest(new GiftCheckoutResponse { Message = "Coach is not connected to Stripe yet." });

                var origin = $"{Request.Scheme}://{Request.Host}";
                var successUrl = string.IsNullOrWhiteSpace(req.SuccessUrl) ? $"{origin}/gift-success?ok=1" : req.SuccessUrl!;
                var cancelUrl = string.IsNullOrWhiteSpace(req.CancelUrl) ? $"{origin}/?gift=cancel" : req.CancelUrl!;
                var productName = $"Gift to {(string.IsNullOrWhiteSpace(req.CoachName) ? "Coach" : req.CoachName)} (#{coachIdInt})";

                long amountMinor = (long)Math.Round(req.Amount * 100m);
                long appFee = (long)Math.Round(amountMinor * 0.20m); // your 20% platform fee

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
                                UnitAmount = amountMinor
                            },
                            Quantity = 1
                        }
                    },
                    SuccessUrl = successUrl,
                    CancelUrl = cancelUrl,
                    PaymentIntentData = new SessionPaymentIntentDataOptions
                    {
                        ApplicationFeeAmount = appFee,
                        TransferData = new SessionPaymentIntentDataTransferDataOptions
                        {
                            Destination = coach.StripeAccountId
                        },
                        Metadata = new Dictionary<string, string>
                        {
                            ["coachId"] = coachIdInt.ToString(),
                            ["coachName"] = req.CoachName ?? "",
                            ["note"] = req.Note ?? ""
                        }
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["coachId"] = coachIdInt.ToString(),
                        ["note"] = req.Note ?? ""
                    }
                };

                var service = new SessionService();
                var session = await service.CreateAsync(options);

                return Ok(new GiftCheckoutResponse { Url = session.Url, Message = "ok" });
            }
            catch (StripeException se)
            {
                return StatusCode(502, new GiftCheckoutResponse
                {
                    Message = $"Stripe error creating checkout session: {se.StripeError?.Message ?? se.Message}"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new GiftCheckoutResponse { Message = $"Server error: {ex.Message}" });
            }
        }

        // =====================================================================
        // Webhook: record gifts (session completed) & handle refunds
        // =====================================================================

        [HttpPost("webhook/stripe")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var whSecret = _cfg["Stripe:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(whSecret))
                return BadRequest(new { message = "Webhook secret not configured" });

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], whSecret);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Webhook signature verification failed", error = ex.Message });
            }

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    {
                        var session = stripeEvent.Data.Object as Session;
                        if (session != null)
                        {
                            var coachIdStr = session.Metadata?["coachId"];
                            if (TryCoachId(coachIdStr, out var coachIdInt))
                            {
                                var note = session.Metadata?["note"];
                                var amount = session.AmountTotal ?? 0;
                                var donorEmail = session.CustomerDetails?.Email;

                                _db.CoachGifts.Add(new CoachGift
                                {
                                    CoachId = coachIdInt,
                                    AmountMinor = amount,
                                    Currency = (session.Currency ?? "aed").ToUpper(),
                                    Note = string.IsNullOrWhiteSpace(note) ? null : note,
                                    StripeSessionId = session.Id,
                                    StripePaymentIntentId = session.PaymentIntentId,
                                    Status = "succeeded",
                                    DonorEmail = donorEmail,
                                    CreatedUtc = DateTime.UtcNow
                                });
                                await _db.SaveChangesAsync();
                            }
                        }
                        break;
                    }

                case "charge.refunded":
                    {
                        var charge = stripeEvent.Data.Object as Charge;
                        if (charge != null)
                        {
                            var pi = charge.PaymentIntentId;
                            var gift = await _db.CoachGifts.FirstOrDefaultAsync(g => g.StripePaymentIntentId == pi);
                            if (gift != null)
                            {
                                gift.Status = "refunded";
                                await _db.SaveChangesAsync();
                            }
                        }
                        break;
                    }
            }

            return Ok();
        }
    }
}
