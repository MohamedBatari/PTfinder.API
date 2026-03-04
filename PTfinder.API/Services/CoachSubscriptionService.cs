using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Settings;
using Stripe;

namespace PTfinder.API.Services
{
    public class CoachSubscriptionService : ICoachSubscriptionService
    {
        private readonly AppDbContext _db;
        private readonly StripeSettings _stripe;

        public CoachSubscriptionService(AppDbContext db, IOptions<StripeSettings> stripeOptions)
        {
            _db = db;
            _stripe = stripeOptions.Value;
        }

        public async Task HandleCheckoutCompletedAsync(
            string coachIdString,
            string plan,
            string stripeCustomerId,
            string stripeSubscriptionId,
            DateTime? currentPeriodEndUtc)
        {
            if (!int.TryParse((coachIdString ?? "").Trim(), out var coachId) || coachId <= 0)
                return;

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId);
            if (coach == null) return;

            var now = DateTime.UtcNow;

            coach.StripeCustomerId = stripeCustomerId;
            coach.StripeSubscriptionId = stripeSubscriptionId;

            coach.SubscriptionTier = (plan ?? "").Trim().ToLowerInvariant() switch
            {
                "basic" => SubscriptionTier.Basic,
                "pro" => SubscriptionTier.Standard,
                "standard" => SubscriptionTier.Standard,
                _ => SubscriptionTier.None
            };

            coach.SubscriptionStatus = SubscriptionStatus.Active;
            coach.IsActive = true;

            coach.SubscriptionStartedAtUtc ??= now;

            var end = NormalizeUtc(currentPeriodEndUtc);
            if (IsValidEnd(end))
            {
                coach.CurrentPeriodEndUtc = end;
                coach.SubscriptionExpiresAtUtc = end;
            }

            // new checkout = active intent
            coach.CancelAtPeriodEnd = false;
            coach.CanceledAtUtc = null;

            coach.UpdatedAtUtc = now;
            await _db.SaveChangesAsync();
        }

        // ✅ NEW: takes full Stripe.Subscription
        public async Task UpdateFromSubscriptionEventAsync(Stripe.Subscription sub)
        {
            if (sub == null || string.IsNullOrWhiteSpace(sub.Id)) return;

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.StripeSubscriptionId == sub.Id);
            if (coach == null) return;

            // 1) status mapping
            var s = (sub.Status ?? "").Trim().ToLowerInvariant();
            coach.SubscriptionStatus = s switch
            {
                "active" => SubscriptionStatus.Active,
                "trialing" => SubscriptionStatus.Active,
                "past_due" => SubscriptionStatus.PastDue,
                "unpaid" => SubscriptionStatus.PastDue,
                "canceled" => SubscriptionStatus.Inactive,
                "incomplete" => SubscriptionStatus.Inactive,
                "incomplete_expired" => SubscriptionStatus.Inactive,
                _ => SubscriptionStatus.Inactive
            };

            if (coach.SubscriptionStatus == SubscriptionStatus.Inactive)
            {
                coach.SubscriptionTier = SubscriptionTier.None;
            }

            coach.IsActive = true;

            // 2) end dates
            var end = NormalizeUtc(sub.CurrentPeriodEnd);
            if (IsValidEnd(end))
            {
                coach.CurrentPeriodEndUtc = end;
                coach.SubscriptionExpiresAtUtc = end;
            }

            // 3) cancel scheduling (this is what you were missing)
            coach.CancelAtPeriodEnd = sub.CancelAtPeriodEnd;
            coach.CanceledAtUtc = NormalizeUtc(sub.CanceledAt);

            // 4) update tier based on the current price id (UPGRADE/DOWNGRADE FIX)
            var priceId = sub.Items?.Data?.FirstOrDefault()?.Price?.Id;

            // ✅ Only set tier from price if subscription is NOT inactive
            if (coach.SubscriptionStatus != SubscriptionStatus.Inactive && !string.IsNullOrWhiteSpace(priceId))
            {
                coach.SubscriptionTier = MapTierFromPriceId(priceId);
                coach.SubscriptionExpiresAtUtc = coach.CurrentPeriodEndUtc;
            }

            coach.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            Console.WriteLine($"SERVICE UpdateFromSubscriptionEventAsync: sub={sub.Id} status={sub.Status} cancelAtPeriodEnd={sub.CancelAtPeriodEnd} price={priceId} end={sub.CurrentPeriodEnd:O}");
        }

        private SubscriptionTier MapTierFromPriceId(string priceId)
        {
            if (priceId == _stripe.BasicMonthlyPriceId || priceId == _stripe.BasicYearlyPriceId)
                return SubscriptionTier.Basic;

            if (priceId == _stripe.ProMonthlyPriceId || priceId == _stripe.ProYearlyPriceId)
                return SubscriptionTier.Standard;

            return SubscriptionTier.None;
        }

        private static DateTime? NormalizeUtc(DateTime? dt)
        {
            if (!dt.HasValue) return null;

            if (dt.Value.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);

            return dt.Value.ToUniversalTime();
        }

        private static bool IsValidEnd(DateTime? dt)
        {
            if (!dt.HasValue) return false;
            return dt.Value >= new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }
}