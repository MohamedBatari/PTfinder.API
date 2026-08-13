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
        private readonly IPushNotificationSender _push;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            AppDbContext db,
            IHubContext<NotifyHub> hub,
            IPushNotificationSender push,
            ILogger<NotificationService> logger)
        {
            _db = db;
            _hub = hub;
            _push = push;
            _logger = logger;
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

            await SendBackgroundPushAsync(n, ct);

            return n;
        }

        private async Task SendBackgroundPushAsync(Notification n, CancellationToken ct)
        {
            try
            {
                var tokens = await _db.PushDevices
                    .AsNoTracking()
                    .Where(x => x.IsActive &&
                        ((n.RecipientKind == RecipientKind.Coach && n.CoachId.HasValue && x.CoachId == n.CoachId.Value) ||
                         (n.RecipientKind == RecipientKind.Client && n.ClientId.HasValue && x.ClientId == n.ClientId.Value)))
                    .Select(x => x.Token)
                    .ToListAsync(ct);

                if (tokens.Count == 0) return;

                var data = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["notificationId"] = n.Id.ToString(),
                    ["type"] = n.Type,
                    ["link"] = n.Link ?? string.Empty
                };
                if (!string.IsNullOrWhiteSpace(n.MetadataJson))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(n.MetadataJson);
                        foreach (var property in document.RootElement.EnumerateObject())
                        {
                            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                                data[property.Name] = property.Value.ToString();
                        }
                    }
                    catch (JsonException) { }
                }

                var channel = n.Type.StartsWith("conversation.", StringComparison.OrdinalIgnoreCase)
                    ? "messages"
                    : "bookings";
                await _push.SendAsync(tokens, n.Title, n.Body, data, channel, ct);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException)
            {
                // Push delivery is best-effort. Never fail a booking/message
                // because a token table or external gateway is unavailable.
                _logger.LogWarning(ex, "Background push could not be sent for notification {NotificationId}.", n.Id);
            }
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

        public async Task NotifyClientConversationMessage(
            int clientId, int conversationId, string coachName, CancellationToken ct = default)
        {
            await CreateAsync(new Notification
            {
                RecipientKind = RecipientKind.Client,
                ClientId = clientId,
                Type = "conversation.reply",
                Title = $"New message from {coachName}",
                Body = "Open your messages to read the coach's reply.",
                Link = $"/messages/{conversationId}",
                MetadataJson = JsonSerializer.Serialize(new { conversationId })
            }, ct);
        }

    }
}

