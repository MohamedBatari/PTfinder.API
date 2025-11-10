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
                CoachId = coachId,
                Title = title,
                Body = body,
                Link = link,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false
            };
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync(ct);

            await _hub.Clients.Group($"coach-{coachId}")
                .SendAsync("notification", new
                {
                    id = n.Id,
                    title = n.Title,
                    body = n.Body,
                    link = n.Link,
                    createdAtUtc = n.CreatedAtUtc
                }, ct);
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
                CoachId = coachId,
                Title = title,
                Body = body,
                Link = link,
                CreatedAtUtc = DateTime.UtcNow,
                IsRead = false
            };
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync(ct);

            await _hub.Clients.Group($"coach-{coachId}")
                .SendAsync("notification", new
                {
                    id = n.Id,
                    title = n.Title,
                    body = n.Body,
                    link = n.Link,
                    createdAtUtc = n.CreatedAtUtc
                }, ct);
        }

    }
}

