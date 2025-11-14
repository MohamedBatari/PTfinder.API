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
            if (!int.TryParse(coachIdString, out var coachId))
                return;

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId);
            if (coach == null) return;

            var now = DateTime.UtcNow;

            coach.StripeCustomerId = stripeCustomerId;
            coach.StripeSubscriptionId = stripeSubscriptionId;

            coach.SubscriptionTier = plan.ToLower() switch
            {
                "basic" => SubscriptionTier.Basic,
                "pro" => SubscriptionTier.Standard,
                _ => SubscriptionTier.None
            };

            coach.SubscriptionStatus = SubscriptionStatus.Active;
            coach.SubscriptionStartedAtUtc = now;

            if (currentPeriodEndUtc.HasValue)
            {
                coach.CurrentPeriodEndUtc = currentPeriodEndUtc.Value;
                coach.SubscriptionExpiresAtUtc = currentPeriodEndUtc.Value;
            }

            coach.UpdatedAtUtc = now;
            await _db.SaveChangesAsync();
        }

        public async Task UpdateFromSubscriptionEventAsync(
            string stripeSubscriptionId,
            string status,
            DateTime? currentPeriodEndUtc)
        {
            var coach = await _db.Coaches
                .FirstOrDefaultAsync(c => c.StripeSubscriptionId == stripeSubscriptionId);

            if (coach == null) return;

            coach.SubscriptionStatus = status switch
            {
                "active" => SubscriptionStatus.Active,
                "trialing" => SubscriptionStatus.Active,
                "past_due" => SubscriptionStatus.PastDue,
                "canceled" => SubscriptionStatus.Canceled,
                "unpaid" => SubscriptionStatus.PastDue,
                _ => coach.SubscriptionStatus
            };

            if (currentPeriodEndUtc.HasValue)
            {
                coach.CurrentPeriodEndUtc = currentPeriodEndUtc.Value;
                coach.SubscriptionExpiresAtUtc = currentPeriodEndUtc.Value;
            }

            coach.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}


