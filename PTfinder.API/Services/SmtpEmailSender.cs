using System.Net;
using System.Net.Mail;
using System.Text;
using PTfinder.API.Settings;

namespace PTfinder.API.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _cfg;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(SmtpSettings cfg, ILogger<SmtpEmailSender> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string? htmlBody,
        string? textBody,
        CancellationToken ct = default,
        Dictionary<string, string>? headers = null,
        IEnumerable<(string FileName, string ContentType, byte[] Bytes)>? attachments = null,
        IEnumerable<(string Name, string Value)>? tags = null,
        string? fromOverride = null)
    {
        var from = new MailAddress(
            string.IsNullOrWhiteSpace(fromOverride) ? _cfg.FromAddresses.Default : fromOverride,
            "PTfinderNow");

        using var msg = new MailMessage { From = from, Subject = subject, BodyEncoding = Encoding.UTF8 };
        msg.To.Add(to);

        // Reply-To
        if (!string.IsNullOrWhiteSpace(_cfg.ReplyTo))
            msg.ReplyToList.Add(new MailAddress(_cfg.ReplyTo));

        // Multipart/alternative (text + html)
        if (!string.IsNullOrWhiteSpace(textBody))
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(textBody, Encoding.UTF8, "text/plain"));
        if (!string.IsNullOrWhiteSpace(htmlBody))
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html"));

        // Headers
        if (headers != null)
            foreach (var kv in headers)
                msg.Headers[kv.Key] = kv.Value;

        // Optional tagging -> custom headers (shows in webhooks)
        if (tags != null)
            foreach (var (Name, Value) in tags)
                msg.Headers[$"X-PTN-{Name}"] = Value;

        // Attachments
        if (attachments != null)
        {
            foreach (var (FileName, ContentType, Bytes) in attachments)
            {
                var ms = new MemoryStream(Bytes);
                var att = new Attachment(ms, ContentType) { Name = FileName };
                msg.Attachments.Add(att);
            }
        }

        using var client = new SmtpClient(_cfg.Host, _cfg.Port)
        {
            EnableSsl = true, // STARTTLS on 2525/587
            Credentials = new NetworkCredential(_cfg.User, _cfg.Pass),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        _log.LogInformation("SMTP send via {Host}:{Port} From={From} To={To}", _cfg.Host, _cfg.Port, from.Address, to);
        try
        {
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(msg, ct);
        }
        catch (SmtpException ex)
        {
            _log.LogError(ex, "SMTP send failed");
            throw new InvalidOperationException($"SMTP send failed: {ex.StatusCode} - {ex.Message}", ex);
        }
    }
}

