// PTfinder.API/Services/ICoachSubscriptionService.cs
using System;
using System.Threading.Tasks;

namespace PTfinder.API.Services
{
    public interface ICoachSubscriptionService
    {
        Task HandleCheckoutCompletedAsync(
            string coachIdString,
            string plan,
            string stripeCustomerId,
            string stripeSubscriptionId,
            DateTime? currentPeriodEndUtc);

        Task UpdateFromSubscriptionEventAsync(
            string stripeSubscriptionId,
            string status,
            DateTime? currentPeriodEndUtc);
    }
}
