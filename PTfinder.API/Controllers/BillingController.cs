using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Services;      // IEmailSender + ICoachSubscriptionService
using PTfinder.API.Settings;     // StripeSettings
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
        private readonly IEmailSender _email;
        private readonly StripeSettings _stripeSettings;
        private readonly ICoachSubscriptionService _coachSubscriptions;

        public BillingController(
            AppDbContext db,
            IConfiguration cfg,
            IEmailSender email,
            IOptions<StripeSettings> stripeOptions,
            ICoachSubscriptionService coachSubscriptions)
        {
            _db = db;
            _cfg = cfg;
            _email = email;
            _stripeSettings = stripeOptions.Value;
            _coachSubscriptions = coachSubscriptions;

            if (string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey))
                StripeConfiguration.ApiKey = _cfg["Stripe:SecretKey"];
        }

        private string FrontendBase =>
            _stripeSettings.FrontendBase?.TrimEnd('/') ??
            _cfg["Stripe:FrontendBase"]?.TrimEnd('/') ??
            $"{Request.Scheme}://{Request.Host}";

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

                if (!string.IsNullOrWhiteSpace(coach.StripeAccountId))
                    return Ok(new { accountId = coach.StripeAccountId });

                var acctSvc = new AccountService();
                var acct = await acctSvc.CreateAsync(new AccountCreateOptions
                {
                    Country = "AE",
                    Type = "express",
                    Email = string.IsNullOrWhiteSpace(coach.Email) ? null : coach.Email,
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
                        hint = "Stripe Dashboard → (Test mode ON) → Settings → Connect → Enable Connect (Express). Then retry.",
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
        public async Task<IActionResult> CreateAccountLink([FromBody] ConnectAccountRequest body)
        {
            if (!TryCoachId(body?.CoachId, out var coachIdInt))
                return BadRequest(new { message = "coachId must be an integer" });

            var key = _cfg["Stripe:SecretKey"];
            if (string.IsNullOrWhiteSpace(key))
                return StatusCode(500, new { message = "Stripe not configured (SecretKey missing)" });

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachIdInt);
            if (coach == null) return NotFound(new { message = "Coach not found" });

            var accountId = coach.StripeAccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                var acctSvc = new AccountService();
                var acct = await acctSvc.CreateAsync(new AccountCreateOptions
                {
                    Country = "AE",
                    Type = "express",
                    Email = string.IsNullOrWhiteSpace(coach.Email) ? null : coach.Email,
                    Capabilities = new AccountCapabilitiesOptions
                    {
                        Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
                    }
                });
                coach.StripeAccountId = acct.Id;
                await _db.SaveChangesAsync();
                accountId = acct.Id;
            }

            var appBase = FrontendBase;
            var links = new AccountLinkService();
            var link = await links.CreateAsync(new AccountLinkCreateOptions
            {
                Account = accountId!,
                Type = "account_onboarding",
                RefreshUrl = $"{appBase}/onboarding/refresh",
                ReturnUrl = $"{appBase}/onboarding/return"
            });

            return Ok(new { url = link.Url });
        }

        // POST /api/Billing/connect/login-link
        [HttpPost("connect/login-link")]
        public async Task<IActionResult> CreateLoginLink([FromBody] ConnectAccountRequest body)
        {
            if (!TryCoachId(body?.CoachId, out var coachIdInt))
                return BadRequest(new { message = "coachId must be an integer" });

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachIdInt);
            if (coach == null) return NotFound(new { message = "Coach not found" });
            if (string.IsNullOrWhiteSpace(coach.StripeAccountId))
                return BadRequest(new { message = "Coach is not connected to Stripe yet." });

            var loginSvc = new LoginLinkService();
            var link = await loginSvc.CreateAsync(coach.StripeAccountId);
            return Ok(new { url = link.Url });
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

                // ✅ Sync Stripe flags into your DB
                // (Use ?? false only if your DB columns are NOT NULL bools)
                coach.StripeChargesEnabled = acct.ChargesEnabled;
                coach.StripePayoutsEnabled = acct.PayoutsEnabled;
                coach.StripeDetailsSubmitted = acct.DetailsSubmitted;

                // Save only if changed (optional but clean)
                await _db.SaveChangesAsync();

                var req = acct.Requirements;
                var disabledReason = (req?.DisabledReason ?? string.Empty).ToLowerInvariant();
                var hasDue = (req?.CurrentlyDue?.Any() ?? false) || (req?.PastDue?.Any() ?? false);
                var canLoginLink = acct.PayoutsEnabled && acct.ChargesEnabled && !hasDue &&
                                   disabledReason is "" or "other";

                return Ok(new
                {
                    connected = true,
                    accountId = acct.Id,
                    chargesEnabled = acct.ChargesEnabled,
                    payoutsEnabled = acct.PayoutsEnabled,
                    detailsSubmitted = acct.DetailsSubmitted, // ✅ add this in response too
                    disabledReason,
                    requirements = req?.CurrentlyDue ?? new List<string>(),
                    pastDue = req?.PastDue ?? new List<string>(),
                    eventuallyDue = req?.EventuallyDue ?? new List<string>(),
                    pendingVerification = req?.PendingVerification ?? new List<string>(),
                    canLoginLink
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
        // SUBSCRIPTIONS: PTfinderNow Basic / Pro
        // =====================================================================

        [HttpPost("subscription/checkout")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<ActionResult<SubscriptionCheckoutResponse>> CreateSubscriptionCheckout(
            [FromBody] SubscriptionCheckoutRequest req)
        {
            if (req == null || req.CoachId <= 0 || string.IsNullOrWhiteSpace(req.Plan))
            {
                return BadRequest(new SubscriptionCheckoutResponse
                {
                    Message = "Invalid request."
                });
            }

            var plan = req.Plan.ToLower().Trim();
            string? priceId = null;

            if (plan == "basic")
            {
                priceId = req.Yearly ? _stripeSettings.BasicYearlyPriceId : _stripeSettings.BasicMonthlyPriceId;
            }
            else if (plan == "pro")
            {
                priceId = req.Yearly ? _stripeSettings.ProYearlyPriceId : _stripeSettings.ProMonthlyPriceId;
            }

            if (string.IsNullOrEmpty(priceId))
            {
                return BadRequest(new SubscriptionCheckoutResponse
                {
                    Message = "Invalid plan or billing period."
                });
            }

            var successUrl = _stripeSettings.SuccessUrl
                ?? $"{FrontendBase}/coach/subscription/success?session_id={{CHECKOUT_SESSION_ID}}";

            var cancelUrl = _stripeSettings.CancelUrl
                ?? $"{FrontendBase}/coach/subscription/cancelled";

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
        {
            new SessionLineItemOptions
            {
                Price = priceId,
                Quantity = 1
            }
        },
                ClientReferenceId = req.CoachId.ToString(),
                Metadata = new Dictionary<string, string>
                {
                    ["plan"] = plan,
                    ["billingPeriod"] = req.Yearly ? "yearly" : "monthly",
                    ["kind"] = "subscription"
                },
                // ✅ 14-day free trial – Stripe will check card, but first charge only after trial end
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    TrialPeriodDays = 14
                }
            };

            var svc = new SessionService();
            var session = await svc.CreateAsync(options);

            return Ok(new SubscriptionCheckoutResponse
            {
                Url = session.Url,
                Message = "Checkout created"
            });
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
                long appFee = (long)Math.Round(amountMinor * 0.20m); // 20% platform fee

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
                        ["note"] = req.Note ?? "",
                        ["kind"] = "gift"
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
        // Webhook: gifts + subscriptions
        // =====================================================================

        [AllowAnonymous]
        [HttpPost("webhook/stripe")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var whSecret = _cfg["Stripe:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(whSecret))
                return BadRequest(new { message = "Webhook secret not configured" });

            if (!Request.Headers.TryGetValue("Stripe-Signature", out var sig) || string.IsNullOrWhiteSpace(sig))
                return BadRequest(new { message = "Missing Stripe-Signature header" });

            Event evt;
            try
            {
                evt = EventUtility.ConstructEvent(
                    json,
                    sig.ToString(),
                    whSecret,
                    tolerance: 300,
                    throwOnApiVersionMismatch: false
                );
            }
            catch (StripeException se)
            {
                Console.WriteLine("WEBHOOK signature fail: " + se.Message);
                return BadRequest(new { message = "Signature verification failed", error = se.Message });
            }

            if (evt.Type == "checkout.session.completed")
            {
                var session = evt.Data.Object as Session;
                if (session != null)
                {
                    if (session.Mode == "payment")
                    {
                        await HandleGiftSessionCompleted(session);
                    }
                    else if (session.Mode == "subscription")
                    {
                        await HandleSubscriptionSessionCompleted(session);
                    }
                }
            }
            else if (evt.Type == Events.CustomerSubscriptionCreated || 
                     evt.Type == Events.CustomerSubscriptionUpdated ||
                     evt.Type == Events.CustomerSubscriptionDeleted)
            {
                var subscription = evt.Data.Object as Stripe.Subscription;
                if (subscription != null)
                {
                    await _coachSubscriptions.UpdateFromSubscriptionEventAsync(
                        subscription.Id,
                        subscription.Status,
                        subscription.CurrentPeriodEnd
                    );
                }
            }
            else if (evt.Type == "charge.refunded")
            {
                var charge = evt.Data.Object as Charge;
                var pi = charge?.PaymentIntentId;
                if (!string.IsNullOrWhiteSpace(pi))
                {
                    var gift = await _db.CoachGifts.FirstOrDefaultAsync(g => g.StripePaymentIntentId == pi);
                    if (gift != null)
                    {
                        gift.Status = "refunded";
                        await _db.SaveChangesAsync();
                    }
                }
            }

            return Ok();
        }

        private async Task HandleSubscriptionSessionCompleted(Session session)
        {
            var plan = session.Metadata != null && session.Metadata.TryGetValue("plan", out var p)
                ? p
                : "basic";

            var coachIdString = session.ClientReferenceId ?? "0";
            var subscriptionId = session.SubscriptionId;
            var customerId = session.CustomerId;

            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                var subSvc = new Stripe.SubscriptionService();
                var sub = await subSvc.GetAsync(subscriptionId);

                // 1) Your existing logic: update internal subscription state
                await _coachSubscriptions.HandleCheckoutCompletedAsync(
                    coachIdString,
                    plan,
                    customerId,
                    subscriptionId,
                    sub.CurrentPeriodEnd
                );

                // 2) New: send email to coach with subscription + invoice info
                if (int.TryParse(coachIdString, out var coachIdInt))
                {
                    var coach = await _db.Coaches.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == coachIdInt);

                    if (coach != null && !string.IsNullOrWhiteSpace(coach.Email))
                    {
                        Invoice? invoice = null;
                        try
                        {
                            var latestInvoiceId = sub.LatestInvoiceId;
                            if (!string.IsNullOrWhiteSpace(latestInvoiceId))
                            {
                                var invSvc = new InvoiceService();
                                invoice = await invSvc.GetAsync(latestInvoiceId);
                            }
                        }
                        catch
                        {
                            // ignore invoice fetch errors
                        }

                        var firstItem = sub.Items?.Data?.FirstOrDefault();
                        var price = firstItem?.Price;
                        var currency = (invoice?.Currency ?? price?.Currency ?? "aed").ToUpperInvariant();
                        long unitAmountMinor = price?.UnitAmount ?? 0;
                        var amount = unitAmountMinor / 100m;

                        var interval = price?.Recurring?.Interval ?? "month";
                        var intervalCount = price?.Recurring?.IntervalCount ?? 1;

                        // Stripe .NET gives DateTime (or DateTime?) – no Unix conversion needed
                        var currentPeriodStart = sub.CurrentPeriodStart;
                        var currentPeriodEnd = sub.CurrentPeriodEnd;

                        DateTime? trialEnd = sub.TrialEnd;

                        var planLabel = plan.Equals("pro", StringComparison.OrdinalIgnoreCase) ? "Pro" : "Basic";
                        var billingPeriodLabel = intervalCount == 1
                            ? interval
                            : $"{intervalCount} {interval}";

                        var hostedInvoiceUrl = invoice?.HostedInvoiceUrl;
                        var invoicePdf = invoice?.InvoicePdf;

                        var subject = "✅ Your PTfinderNow subscription is active";

                        var bodyHtml = $@"
<p>Hi <b>{coach.FullName ?? ""} </b>,</p>
<p>Your PTfinderNow subscription is now <b>active</b>.</p>
<ul>
  <li><b>Plan:</b> {planLabel}</li>
  <li><b>Billing:</b> every {billingPeriodLabel}</li>
  <li><b>Amount:</b> {currency} {amount:0.00}</li>
  <li><b>Current period:</b> {currentPeriodStart:yyyy-MM-dd} → {currentPeriodEnd:yyyy-MM-dd}</li>
  {(trialEnd.HasValue ? $"<li><b>Trial ends:</b> {trialEnd.Value:yyyy-MM-dd}</li>" : "")}
</ul>
{(string.IsNullOrWhiteSpace(hostedInvoiceUrl) ? "" : $"<p><b>Invoice (online):</b> <a href=\"{hostedInvoiceUrl}\">{hostedInvoiceUrl}</a></p>")}
{(string.IsNullOrWhiteSpace(invoicePdf) ? "" : $"<p><b>Invoice PDF:</b> <a href=\"{invoicePdf}\">{invoicePdf}</a></p>")}
<p>You can manage your subscription anytime here: <a href=""{FrontendBase}/coach/subscription"">Subscription Dashboard</a>.</p>
<p>- PTfinderNow</p>";

                        var bodyText =
$@"Hi {coach.FullName ?? "Coach"},

Your PTfinderNow subscription is now active.

Plan: {planLabel}
Billing: every {billingPeriodLabel}
Amount: {currency} {amount:0.00}
Current period: {currentPeriodStart:yyyy-MM-dd} → {currentPeriodEnd:yyyy-MM-dd}" +
(trialEnd.HasValue ? $"\nTrial ends: {trialEnd.Value:yyyy-MM-dd}" : "") +
(string.IsNullOrWhiteSpace(hostedInvoiceUrl) ? "" : $"\nInvoice (online): {hostedInvoiceUrl}") +
(string.IsNullOrWhiteSpace(invoicePdf) ? "" : $"\nInvoice PDF: {invoicePdf}") +
$"\n\nManage your subscription: {FrontendBase}/coach/subscription\n- PTfinderNow";

                        try
                        {
                            await _email.SendAsync(
                                to: coach.Email,
                                subject: subject,
                                htmlBody: bodyHtml,
                                textBody: bodyText,
                                tags: new[] { ("Event", "SubscriptionStarted") }
                            );
                        }
                        catch (Exception mailEx)
                        {
                            Console.WriteLine("Subscription email send error: " + mailEx.Message);
                        }
                    }
                }
            }
        }

        private async Task HandleGiftSessionCompleted(Session session)
        {
            string? coachIdStr = session.Metadata?.TryGetValue("coachId", out var v1) == true ? v1 : null;
            PaymentIntent? pi = null;

            if (string.IsNullOrWhiteSpace(coachIdStr) && !string.IsNullOrWhiteSpace(session.PaymentIntentId))
            {
                try { pi = await new PaymentIntentService().GetAsync(session.PaymentIntentId); }
                catch { /* ignore */ }
                if (pi?.Metadata?.TryGetValue("coachId", out var v2) == true) coachIdStr = v2;
            }
            else if (!string.IsNullOrWhiteSpace(session.PaymentIntentId))
            {
                try { pi = await new PaymentIntentService().GetAsync(session.PaymentIntentId); }
                catch { /* ignore */ }
            }

            if (!int.TryParse(coachIdStr, out var coachIdInt))
                return;

            var already = await _db.CoachGifts.AsNoTracking()
                .AnyAsync(x => x.StripePaymentIntentId == session.PaymentIntentId);
            if (already) return;

            var amountMinor = session.AmountTotal ?? 0;
            var currency = (session.Currency ?? "AED").ToUpperInvariant();
            var note = (session.Metadata?.TryGetValue("note", out var n) == true && !string.IsNullOrWhiteSpace(n)) ? n : null;
            var donor = session.CustomerDetails?.Email;

            _db.CoachGifts.Add(new CoachGift
            {
                CoachId = coachIdInt,
                AmountMinor = amountMinor,
                Currency = currency,
                Note = note,
                StripeSessionId = session.Id,
                StripePaymentIntentId = session.PaymentIntentId,
                Status = "succeeded",
                DonorEmail = donor,
                CreatedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            try
            {
                var coach = await _db.Coaches.AsNoTracking().FirstOrDefaultAsync(c => c.Id == coachIdInt);
                if (coach != null && !string.IsNullOrWhiteSpace(coach.Email))
                {
                    long feeMinor = 0;
                    if (pi != null && pi.ApplicationFeeAmount.HasValue)
                        feeMinor = pi.ApplicationFeeAmount.Value;
                    else
                        feeMinor = (long)Math.Round(amountMinor * 0.20m);

                    var netMinor = Math.Max(0, amountMinor - feeMinor);
                    var gross = amountMinor / 100m;
                    var net = netMinor / 100m;

                    var subject = "🎁 You just received a gift on PTfinderNow";
                    var bodyHtml = $@"
<p>Hi <b>{coach.FullName ?? "Coach"}</b>,</p>
<p>You just received a gift of <b>AED {gross:0.00}</b>{(string.IsNullOrWhiteSpace(donor) ? "" : $" from <b>{donor}</b>")}.</p>
<p>Your payout (80%) is <b>AED {net:0.00}</b>. The remaining 20% is the platform fee.</p>
{(string.IsNullOrWhiteSpace(note) ? "" : $"<p><i>Message from sender:</i> {System.Net.WebUtility.HtmlEncode(note)}</p>")}
<p>You can track payouts in your dashboard: <a href=""{FrontendBase}/dashboard/gifts"">Gifts Dashboard</a>.</p>
<p>- PTfinderNow</p>";

                    var bodyText = $@"Hi {coach.FullName ?? "Coach"},
You received a gift of AED {gross:0.00}{(string.IsNullOrWhiteSpace(donor) ? "" : $" from {donor}")}.
Your payout (80%) is AED {net:0.00}. The remaining 20% is the platform fee.
{(string.IsNullOrWhiteSpace(note) ? "" : $"Message: {note}")}
Dashboard: {FrontendBase}/dashboard/gifts
- PTfinderNow";

                    await _email.SendAsync(
                        to: coach.Email,
                        subject: subject,
                        htmlBody: bodyHtml,
                        textBody: bodyText,
                        tags: new[] { ("Event", "GiftReceived") }
                    );
                }
            }
            catch (Exception mailEx)
            {
                Console.WriteLine("Email send error: " + mailEx.Message);
            }
        }

        [HttpGet("debug/webhook-config")]
        public ActionResult<object> WebhookConfigDebug()
        {
            var secret = _cfg["Stripe:WebhookSecret"];
            return Ok(new
            {
                hasSecret = !string.IsNullOrWhiteSpace(secret),
                secretPreview = secret?.StartsWith("whsec_") == true ? "whsec_***" : secret,
                route = "/api/Billing/webhook/stripe"
            });
        }
    }
}
