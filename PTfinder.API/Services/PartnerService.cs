using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Services
{
    public class PartnerService
    {
        private readonly AppDbContext _db;
        public PartnerService(AppDbContext db) => _db = db;

        public async Task<(bool ok, string? error)> AssignCoachAsync(int partnerId, int coachId)
        {
            var partner = await _db.Partners
                                   .Include(p => p.Coaches)
                                   .FirstOrDefaultAsync(p => p.Id == partnerId);

            if (partner == null) return (false, "Partner not found.");
            if (!partner.IsActive) return (false, "Partner subscription is inactive.");
            if (partner.CurrentPeriodEndUtc.HasValue && partner.CurrentPeriodEndUtc.Value < DateTime.UtcNow)
                return (false, "Partner billing period ended. Renew to add coaches.");

            // Seat cap
            var used = partner.Coaches.Count;
            if (partner.MaxCoaches > 0 && used >= partner.MaxCoaches)
                return (false, $"Seat limit reached ({partner.MaxCoaches}). Upgrade plan or remove a coach.");

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId);
            if (coach == null) return (false, "Coach not found.");

            // Already belongs to this partner?
            if (coach.PartnerId == partnerId) return (true, null);

            // If coach belongs to another partner, block (or detach first)
            if (coach.PartnerId.HasValue && coach.PartnerId != partnerId)
                return (false, "Coach already linked to another partner. Detach first.");

            // Link & grant premium benefits (company sponsored)
            coach.PartnerId = partner.Id;
            coach.SubscriptionTier = SubscriptionTier.Premium;
            coach.SubscriptionStatus = SubscriptionStatus.Active;
            coach.SubscriptionStartedAtUtc ??= DateTime.UtcNow;
            coach.SubscriptionExpiresAtUtc = null; // managed by partner

            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(bool ok, string? error)> RemoveCoachAsync(int partnerId, int coachId)
        {
            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId && c.PartnerId == partnerId);
            if (coach == null) return (false, "Coach is not linked to this partner.");

            coach.PartnerId = null;

            // Optional rule: when detached, stop company benefits.
            // If the coach is NOT a paying freelancer, downgrade.
            if (coach.StripeSubscriptionId is null)
            {
                coach.SubscriptionTier = SubscriptionTier.None;
                coach.SubscriptionStatus = SubscriptionStatus.Inactive;
                coach.SubscriptionExpiresAtUtc = null;
                coach.CurrentPeriodEndUtc = null;
            }

            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<(int total, int active)> GetCoachCountsAsync(int partnerId)
        {
            var total = await _db.Coaches.CountAsync(c => c.PartnerId == partnerId);
            // If you have an IsActive flag on Coach, count it; else same as total
            var active = await _db.Coaches.CountAsync(c => c.PartnerId == partnerId && c.IsActive);
            return (total, active);
        }
    }
}

