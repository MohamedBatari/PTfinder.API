// PTfinder.API/Services/CoachSubscriptionService.cs
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Services
{
    public class CoachSubscriptionService : ICoachSubscriptionService
    {
        private readonly AppDbContext _db;

        public CoachSubscriptionService(AppDbContext db)
        {
            _db = db;
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
                "pro" => SubscriptionTier.Standard,   // pro == standard in your enum
                "standard" => SubscriptionTier.Standard,
                _ => SubscriptionTier.None
            };

            coach.SubscriptionStatus = SubscriptionStatus.Active;
            coach.IsActive = true;

            coach.SubscriptionStartedAtUtc ??= now;

            var end = NormalizeUtc(currentPeriodEndUtc);

            // ✅ never save 1970 / nonsense
            if (IsValidEnd(end))
            {
                coach.CurrentPeriodEndUtc = end;
                coach.SubscriptionExpiresAtUtc = end;
            }

            coach.UpdatedAtUtc = now;
            await _db.SaveChangesAsync();
        }

        public async Task UpdateFromSubscriptionEventAsync(
            string stripeSubscriptionId,
            string status,
            DateTime? currentPeriodEndUtc)
        {
            if (string.IsNullOrWhiteSpace(stripeSubscriptionId)) return;

            var coach = await _db.Coaches
                .FirstOrDefaultAsync(c => c.StripeSubscriptionId == stripeSubscriptionId);

            if (coach == null) return;

            var s = (status ?? "").Trim().ToLowerInvariant();

            coach.SubscriptionStatus = s switch
            {
                "active" => SubscriptionStatus.Active,
                "trialing" => SubscriptionStatus.Active,
                "past_due" => SubscriptionStatus.PastDue,
                "unpaid" => SubscriptionStatus.PastDue,
                "canceled" => SubscriptionStatus.Canceled,
                "incomplete" => SubscriptionStatus.Inactive,
                "incomplete_expired" => SubscriptionStatus.Inactive,
                _ => SubscriptionStatus.Inactive
            };

            // ✅ your Search() requires IsActive
            coach.IsActive = coach.SubscriptionStatus == SubscriptionStatus.Active;

            var end = NormalizeUtc(currentPeriodEndUtc);

            // ✅ do NOT overwrite good dates with 1970
            if (IsValidEnd(end))
            {
                coach.CurrentPeriodEndUtc = end;
                coach.SubscriptionExpiresAtUtc = end;
            }

            coach.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            Console.WriteLine($"SERVICE UpdateFromSubscriptionEventAsync: sub={stripeSubscriptionId} status={status} end={currentPeriodEndUtc:O}");
            await _db.SaveChangesAsync();
            Console.WriteLine("SERVICE SaveChanges OK");
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

            // reject epoch / nonsense values
            return dt.Value >= new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
    }
}



