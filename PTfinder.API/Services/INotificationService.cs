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

        Task NotifyClientBookingRequest(
            int clientId, int bookingId, string coachName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default);

        Task NotifyClientBookingConfirmed(
            int clientId, int bookingId, string coachName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default);

        Task NotifyClientBookingDeclined(
            int clientId, int bookingId, string coachName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default);

        Task NotifyCoachConversationLead(
            int coachId, int conversationId, string title, CancellationToken ct = default);
    }

}

