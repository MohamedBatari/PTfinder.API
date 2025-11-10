namespace PTfinder.API.DATA.Modules
{
    public enum RecipientKind { Coach = 1, Client = 2 }

    public class Notification
    {
        public int Id { get; set; }
        public RecipientKind RecipientKind { get; set; }

        public int? CoachId { get; set; }
        public Coach? Coach { get; set; }

        public int? ClientId { get; set; } // if you have a Client entity, wire it later

        // e.g. "booking.request", "booking.confirmed", ...
        public string Type { get; set; } = "booking.request";

        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? Link { get; set; }                 // e.g. "/dashboard/bookings/123"
        public bool IsRead { get; set; } = false;         // unread by default

        // optional JSON: { bookingId, serviceName, startsAt, ... }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAtUtc { get; set; }
    }
}

