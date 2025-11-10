using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules; // for Coach, Partner
using PTfinder.API.Settings;
using PTfinder.API.Helpers;

using Stripe;
using Stripe.Checkout;

namespace PTfinder.API.Services
{

    public class BillingService
    {
        private readonly bool _mock;

        private readonly AppDbContext _db;
        private readonly StripeSettings _cfg;


        public BillingService(AppDbContext db, IOptions<StripeSettings> cfg, IConfiguration config)
        {
            _db = db;
            _cfg = cfg.Value;
            _mock = string.Equals(config["Billing:Mock"], "true", StringComparison.OrdinalIgnoreCase);


        }


        // --------- PRICE MAPS (replace with your real price_ IDs) ---------

        // Freelancers (Basic/Standard/Premium) monthly/yearly
        private readonly Dictionary<(string tier, string interval), string> _freelancerPrices =
            new(StringTupleComparer.CaseInsensitive)
        {
            { ("basic",   "month"), "price_freelancer_basic_monthly"   }, // AED 49
            { ("basic",   "year"),  "price_freelancer_basic_yearly"    },
            { ("standard","month"), "price_freelancer_standard_monthly" }, // AED 149
            { ("standard","year"),  "price_freelancer_standard_yearly"  },
            { ("premium", "month"), "price_freelancer_premium_monthly"  }, // AED 249
            { ("premium", "year"),  "price_freelancer_premium_yearly"   }
        };

        // Partners (Small/Medium/Large) monthly/yearly + seat caps
        private readonly Dictionary<(string plan, string interval), (string priceId, int seats)> _partnerPrices =
            new(StringTupleComparer.CaseInsensitive)
        {
            { ("small","month"),  ("price_partner_small_monthly", 10) },
            { ("small","year"),   ("price_partner_small_yearly",  10) },
            { ("medium","month"), ("price_partner_medium_monthly",25) },
            { ("medium","year"),  ("price_partner_medium_yearly", 25) },
            { ("large","month"),  ("price_partner_large_monthly", 50) },
            { ("large","year"),   ("price_partner_large_yearly",  50) }
        };

        // --------- CHECKOUT ---------

        public async Task<string> CreateFreelancerCheckoutAsync(int coachId, string tier, string interval)
        {

            if (_mock)
            {
                return $"https://mock-checkout.ptfindernow.local/freelancer?coachId={coachId}&tier={tier}&interval={interval}";
            }

            if (!_freelancerPrices.TryGetValue((tier, interval), out var priceId))
                throw new InvalidOperationException("Unsupported freelancer tier/interval.");

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId && c.PartnerId == null);
            if (coach == null) throw new InvalidOperationException("Coach not found or is company coach.");

            var customerId = coach.StripeCustomerId;
            if (string.IsNullOrEmpty(customerId))
            {
                var cust = await new CustomerService().CreateAsync(new CustomerCreateOptions
                {
                    Email = coach.Email,
                    Name = coach.FullName,
                    Metadata = new Dictionary<string, string> { { "coachId", coach.Id.ToString() } }
                });
                customerId = cust.Id;
                coach.StripeCustomerId = customerId;
                await _db.SaveChangesAsync();
            }

            var session = await new SessionService().CreateAsync(new SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                SuccessUrl = $"{_cfg.SuccessUrl}?type=freelancer&coachId={coach.Id}",
                CancelUrl = _cfg.CancelUrl,
                LineItems = new List<SessionLineItemOptions> {
                    new() { Price = priceId, Quantity = 1 }
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> {
                        { "kind", "freelancer" },
                        { "coachId", coach.Id.ToString() },
                        { "tier", tier },
                        { "interval", interval }
                    }
                }
            });

            return session.Url;
        }

        public async Task<string> CreatePartnerCheckoutAsync(int partnerId, string plan, string interval)
        {

            if (_mock)
            {
                return $"https://mock-checkout.ptfindernow.local/partner?partnerId={partnerId}&plan={plan}&interval={interval}";
            }
            if (!_partnerPrices.TryGetValue((plan, interval), out var pr))
                throw new InvalidOperationException("Unsupported partner plan/interval.");

            var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Id == partnerId);
            if (partner == null) throw new InvalidOperationException("Partner not found.");

            var customerId = partner.StripeCustomerId;
            if (string.IsNullOrEmpty(customerId))
            {
                var cust = await new CustomerService().CreateAsync(new CustomerCreateOptions
                {
                    Email = partner.Email,
                    Name = partner.Name,
                    Metadata = new Dictionary<string, string> { { "partnerId", partner.Id.ToString() } }
                });
                customerId = cust.Id;
                partner.StripeCustomerId = customerId;
                await _db.SaveChangesAsync();
            }

            var session = await new SessionService().CreateAsync(new SessionCreateOptions
            {
                Mode = "subscription",
                Customer = customerId,
                SuccessUrl = $"{_cfg.SuccessUrl}?type=partner&partnerId={partner.Id}",
                CancelUrl = _cfg.CancelUrl,
                LineItems = new List<SessionLineItemOptions> {
                    new() { Price = pr.priceId, Quantity = 1 }
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> {
                        { "kind", "partner" },
                        { "partnerId", partner.Id.ToString() },
                        { "plan", plan },
                        { "interval", interval },
                        { "seats", pr.seats.ToString() }
                    }
                }
            });

            return session.Url;
        }

        public async Task<string> CreateBillingPortalAsync(string customerId, string returnUrl)
        {
            if (_mock)
            {
                return $"https://mock-portal.ptfindernow.local?customerId={customerId}";
            }
            var svc = new Stripe.BillingPortal.SessionService();
            var sess = await svc.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl
            });
            return sess.Url;
        }

        // --------- WEBHOOK SYNC ---------

        public async Task SyncStripeSubscriptionAsync(global::Stripe.Subscription sub)
        {
            var periodEndUtc = GetCurrentPeriodEndUtc(sub);
            var kind = sub.Metadata?.GetValueOrDefault("kind");
            if (_mock) return;

            if (string.Equals(kind, "partner", StringComparison.OrdinalIgnoreCase))
            {
                var partnerId = int.Parse(sub.Metadata["partnerId"]);
                var partner = await _db.Partners.FirstOrDefaultAsync(p => p.Id == partnerId);
                if (partner == null) return;

                partner.StripeSubscriptionId = sub.Id;
                partner.StripePriceId = sub.Items.Data.FirstOrDefault()?.Price?.Id;
                partner.CurrentPeriodEndUtc = periodEndUtc;
                partner.IsActive = sub.Status is "active" or "trialing";

                var kv = _partnerPrices.FirstOrDefault(k => k.Value.priceId == partner.StripePriceId);
                if (!kv.Equals(default(KeyValuePair<(string, string), (string, int)>)))
                {
                    partner.MaxCoaches = kv.Value.seats;
                    partner.PlanName = kv.Key.Item1; // small/medium/large
                }

                await _db.SaveChangesAsync();
                return;
            }

            if (string.Equals(kind, "freelancer", StringComparison.OrdinalIgnoreCase))
            {
                var coachId = int.Parse(sub.Metadata["coachId"]);
                var tierStr = sub.Metadata.GetValueOrDefault("tier");
                var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId);
                if (coach == null) return;

                coach.StripeSubscriptionId = sub.Id;
                coach.CurrentPeriodEndUtc = periodEndUtc;

                coach.SubscriptionTier = tierStr?.ToLower() switch
                {
                    "basic" => SubscriptionTier.Basic,
                    "standard" => SubscriptionTier.Standard,
                    "premium" => SubscriptionTier.Premium,
                    _ => coach.SubscriptionTier
                };

                coach.SubscriptionStatus = sub.Status switch
                {
                    "active" => SubscriptionStatus.Active,
                    "trialing" => SubscriptionStatus.Active,
                    "past_due" => SubscriptionStatus.PastDue,
                    "canceled" => SubscriptionStatus.Canceled,
                    "unpaid" => SubscriptionStatus.PastDue,
                    _ => SubscriptionStatus.Inactive
                };

                await _db.SaveChangesAsync();
            }
        }

        // --- Helper: make SDK-version-agnostic for CurrentPeriodEnd ---
        // --- Helper: SDK-version-agnostic CurrentPeriodEnd extraction ---
        private static DateTime? GetCurrentPeriodEndUtc(global::Stripe.Subscription sub)
        {
            // Try property named "CurrentPeriodEnd"
            var prop = sub.GetType().GetProperty("CurrentPeriodEnd");
            if (prop != null)
            {
                var val = prop.GetValue(sub);
                if (val is DateTime dt)
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

                // Nullable DateTime boxed appears as DateTime when HasValue, otherwise null
                if (val == null)
                    return null;

                // Some SDKs expose unix seconds (long) here
                if (val is long l1)
                    return DateTimeOffset.FromUnixTimeSeconds(l1).UtcDateTime;

                // If it’s a Nullable<long>, it will be boxed as long when HasValue, or null when not.
                if (val is long?)
                {
                    var nl = (long?)val;
                    if (nl.HasValue)
                        return DateTimeOffset.FromUnixTimeSeconds(nl.Value).UtcDateTime;
                }
            }

            // Try property named "CurrentPeriodEndUnix"
            prop = sub.GetType().GetProperty("CurrentPeriodEndUnix");
            if (prop != null)
            {
                var val = prop.GetValue(sub);
                if (val is long l2)
                    return DateTimeOffset.FromUnixTimeSeconds(l2).UtcDateTime;

                if (val is long?)
                {
                    var nl2 = (long?)val;
                    if (nl2.HasValue)
                        return DateTimeOffset.FromUnixTimeSeconds(nl2.Value).UtcDateTime;
                }
            }

            return null;
        }
    }
}
