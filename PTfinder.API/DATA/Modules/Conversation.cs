using PTfinder.API.Models;

namespace PTfinder.API.DATA.Modules
{
    public enum ConversationSenderKind
    {
        Client = 1,
        Coach = 2
    }

    // One private thread exists for each client/coach pair.  Contact details are
    // deliberately not stored in messages or returned by the conversation API.
    public class Conversation
    {
        public int Id { get; set; }
        public int CoachId { get; set; }
        public Coach Coach { get; set; } = null!;
        public int ClientId { get; set; }
        public Client Client { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CoachReadAtUtc { get; set; }
        public DateTime? ClientReadAtUtc { get; set; }
        public ICollection<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
    }

    public class ConversationMessage
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public Conversation Conversation { get; set; } = null!;
        public ConversationSenderKind SenderKind { get; set; }
        public string Body { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAtUtc { get; set; }
    }
}
