using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Services
{
    public interface INotificationService
    {
        Task NotifyCoachBookingRequest(
            int coachId, int bookingId, string clientName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default);

        Task NotifyCoachBookingConfirmed(
            int coachId, int bookingId, string clientName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default);

        Task NotifyCoachBookingDeclined(
            int coachId, int bookingId, string clientName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default);
    }

}

