using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;

namespace PTfinder.API.Services.Emails;

public interface IConversationEmailFlows
{
    Task SendNewLeadEmail(int conversationId, CancellationToken ct = default);
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

    public async Task SendNewLeadEmail(int conversationId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(x => x.Coach)
            .Include(x => x.Client)
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == conversationId, ct);

        if (conversation?.Coach == null || string.IsNullOrWhiteSpace(conversation.Coach.Email))
            return;

        var latest = conversation.Messages
            .Where(x => x.SenderKind == DATA.Modules.ConversationSenderKind.Client)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        if (latest == null) return;

        var clientName = conversation.Client?.FullName ?? "A new client";
        var clientEmail = conversation.Client?.Email ?? "Not provided";
        var inboxUrl = $"{WebBaseUrl.TrimEnd('/')}/dashboard/inbox?conversation={conversation.Id}";
        var html = EmailTemplates.ConversationLeadCoachHtml(
            conversation.Coach.FullName ?? "Coach",
            clientName,
            clientEmail,
            latest.Body,
            inboxUrl,
            LogoUrl);
        var text = $"New client lead\n\n{clientName} ({clientEmail}) sent:\n{latest.Body}\n\nOpen your inbox: {inboxUrl}\n\n— PTfinderNow";

        await _sender.SendAsync(
            to: conversation.Coach.Email,
            subject: $"New client lead from {clientName} — PTfinderNow",
            htmlBody: html,
            textBody: text,
            ct: ct);
    }
}
