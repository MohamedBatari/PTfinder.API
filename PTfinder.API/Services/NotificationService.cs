using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Hubs;
using PTfinder.API.Services;

namespace PTfinder.API.Services
{
    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<NotifyHub> _hub;

        public NotificationService(AppDbContext db, IHubContext<NotifyHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        public async Task<Notification> CreateAsync(Notification n, CancellationToken ct = default)
        {
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync(ct);

            var payload = new
            {
                id = n.Id,
                n.Type,
                n.Title,
                n.Body,
                n.CreatedAtUtc,
                n.ReadAtUtc,
                n.MetadataJson
            };

            if (n.RecipientKind == RecipientKind.Coach && n.CoachId.HasValue)
                await _hub.Clients.Group($"coach:{n.CoachId.Value}").SendAsync("notify", payload, ct);
            else if (n.RecipientKind == RecipientKind.Client && n.ClientId.HasValue)
                await _hub.Clients.Group($"client:{n.ClientId.Value}").SendAsync("notify", payload, ct);

            return n;
        }

        public async Task NotifyCoachBookingRequest(
            int coachId, int bookingId, string clientName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default)
        {
            var body = $"{clientName} requested {serviceName} on {startsAtLocal:yyyy-MM-dd HH:mm} ({timezone}).";

            var n = new Notification
            {
                RecipientKind = RecipientKind.Coach,
                CoachId = coachId,
                Type = "booking.request",
                Title = "New booking request",
                Body = body,
                MetadataJson = JsonSerializer.Serialize(new { bookingId })
            };

            await CreateAsync(n, ct);
        }

        // Services/NotificationService.cs  (only the new methods shown)
        public async Task NotifyCoachBookingConfirmed(
            int coachId, int bookingId, string clientName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default)
        {
            var title = "Booking confirmed";
            var when = $"{startsAtLocal:yyyy-MM-dd HH:mm} ({timezone})";
            var body = $"{clientName} is confirmed for {serviceName} — {when}.";
            var link = $"/dashboard/bookings/{bookingId}";

            var n = new Notification
            {
                RecipientKind = RecipientKind.Coach,
                CoachId = coachId,
                Type = "booking.confirmed",
                Title = title,
                Body = body,
                Link = link,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false,
                MetadataJson = JsonSerializer.Serialize(new { bookingId })
            };
            await CreateAsync(n, ct);
        }

        public async Task NotifyCoachBookingDeclined(
            int coachId, int bookingId, string clientName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default)
        {
            var title = "Booking declined";
            var when = $"{startsAtLocal:yyyy-MM-dd HH:mm} ({timezone})";
            var body = $"You declined {serviceName} for {clientName} — {when}.";
            var link = $"/dashboard/bookings/{bookingId}";

            var n = new Notification
            {
                RecipientKind = RecipientKind.Coach,
                CoachId = coachId,
                Type = "booking.declined",
                Title = title,
                Body = body,
                Link = link,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false,
                MetadataJson = JsonSerializer.Serialize(new { bookingId })
            };
            await CreateAsync(n, ct);
        }

        public async Task NotifyClientBookingRequest(
            int clientId, int bookingId, string coachName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default)
        {
            var when = $"{startsAtLocal:yyyy-MM-dd HH:mm} ({timezone})";
            await CreateAsync(new Notification
            {
                RecipientKind = RecipientKind.Client,
                ClientId = clientId,
                Type = "booking.request.sent",
                Title = "Booking request sent",
                Body = $"Your {serviceName} request to {coachName} is waiting for confirmation — {when}.",
                Link = $"/bookings/{bookingId}",
                MetadataJson = JsonSerializer.Serialize(new { bookingId })
            }, ct);
        }

        public async Task NotifyClientBookingConfirmed(
            int clientId, int bookingId, string coachName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default)
        {
            var when = $"{startsAtLocal:yyyy-MM-dd HH:mm} ({timezone})";
            await CreateAsync(new Notification
            {
                RecipientKind = RecipientKind.Client,
                ClientId = clientId,
                Type = "booking.confirmed",
                Title = "Booking confirmed",
                Body = $"{coachName} confirmed your {serviceName} — {when}.",
                Link = $"/bookings/{bookingId}",
                MetadataJson = JsonSerializer.Serialize(new { bookingId })
            }, ct);
        }

        public async Task NotifyClientBookingDeclined(
            int clientId, int bookingId, string coachName, string serviceName,
            DateTime startsAtLocal, string timezone, CancellationToken ct = default)
        {
            var when = $"{startsAtLocal:yyyy-MM-dd HH:mm} ({timezone})";
            await CreateAsync(new Notification
            {
                RecipientKind = RecipientKind.Client,
                ClientId = clientId,
                Type = "booking.declined",
                Title = "Booking update",
                Body = $"{coachName} could not confirm your {serviceName} request — {when}.",
                Link = $"/bookings/{bookingId}",
                MetadataJson = JsonSerializer.Serialize(new { bookingId })
            }, ct);
        }

        public async Task NotifyCoachConversationLead(
            int coachId, int conversationId, string title, CancellationToken ct = default)
        {
            var notification = new Notification
            {
                RecipientKind = RecipientKind.Coach,
                CoachId = coachId,
                Type = "conversation.lead",
                Title = title,
                Body = "Open your inbox to view and reply to this client conversation.",
                Link = $"/dashboard/inbox?conversation={conversationId}",
                MetadataJson = JsonSerializer.Serialize(new { conversationId })
            };

            await CreateAsync(notification, ct);
        }

    }
}

