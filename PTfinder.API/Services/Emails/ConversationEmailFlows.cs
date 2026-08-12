using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Services.Emails;

public interface IConversationEmailFlows
{
    Task SendNewLeadEmail(int conversationId, int messageId, CancellationToken ct = default);
}

public sealed class ConversationEmailFlows : IConversationEmailFlows
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly IConfiguration _cfg;

    public ConversationEmailFlows(AppDbContext db, IEmailSender sender, IConfiguration cfg)
    {
        _db = db;
        _sender = sender;
        _cfg = cfg;
    }

    private string WebBaseUrl => _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";
    private string LogoUrl => _cfg["Branding:LogoUrl"] ?? $"{WebBaseUrl.TrimEnd('/')}/images/PtFinderNow.png";

    public async Task SendNewLeadEmail(int conversationId, int messageId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(x => x.Coach)
            .Include(x => x.Client)
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == conversationId, ct);

        if (conversation?.Coach == null || string.IsNullOrWhiteSpace(conversation.Coach.Email))
            return;

        // Use the exact first lead message captured by the controller. This
        // prevents a later message arriving before Hangfire from changing the
        // contents of the one-and-only lead email.
        var firstLeadMessage = conversation.Messages.FirstOrDefault(x =>
            x.Id == messageId && x.SenderKind == ConversationSenderKind.Client);
        if (firstLeadMessage == null) return;

        var fullName = conversation.Client?.FullName?.Trim();
        var firstName = string.IsNullOrWhiteSpace(fullName)
            ? "A new client"
            : fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var inboxUrl = $"{WebBaseUrl.TrimEnd('/')}/dashboard/inbox?conversation={conversation.Id}";
        var html = EmailTemplates.ConversationLeadCoachHtml(
            conversation.Coach.FullName ?? "Coach",
            firstName,
            firstLeadMessage.Body,
            inboxUrl,
            LogoUrl);
        var text = $"New client lead\n\n{firstName} sent:\n{firstLeadMessage.Body}\n\nOpen your inbox: {inboxUrl}\n\n- PTfinderNow";

        await _sender.SendAsync(
            to: conversation.Coach.Email,
            subject: $"New client lead from {firstName} - PTfinderNow",
            htmlBody: html,
            textBody: text,
            ct: ct);
    }
}
