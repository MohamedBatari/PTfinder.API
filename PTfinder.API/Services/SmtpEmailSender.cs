using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
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

    /* ----------------------- Helpers ----------------------- */

    private static string? ExtractEmail(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var s = input.Trim();

        // Match: Name <email@domain>
        var m = Regex.Match(s, @"<\s*([^>\s]+@[^>\s]+)\s*>");
        if (m.Success) return m.Groups[1].Value;

        return s;
    }

    private static MailAddress RequireAddress(string? raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException($"Invalid {label} email: '<null>'");

        try
        {
            if (raw.Contains("<") && raw.Contains(">"))
                return new MailAddress(raw.Trim());

            var email = ExtractEmail(raw);
            if (!string.IsNullOrWhiteSpace(email) &&
                MailAddress.TryCreate(email, out var addr))
                return addr;
        }
        catch { }

        throw new FormatException($"Invalid {label} email: '{raw}'");
    }

    private MailAddress ResolveFrom()
    {
        if (string.IsNullOrWhiteSpace(_cfg.From))
            throw new InvalidOperationException("SMTP setting 'From' is missing.");

        return RequireAddress(_cfg.From, "From");
    }

    private MailAddress? ResolveReplyTo()
    {
        if (string.IsNullOrWhiteSpace(_cfg.ReplyTo)) return null;
        return RequireAddress(_cfg.ReplyTo, "ReplyTo");
    }

    private MailAddress? ResolveBcc()
    {
        if (string.IsNullOrWhiteSpace(_cfg.Bcc)) return null;
        return RequireAddress(_cfg.Bcc, "Bcc");
    }

    private static bool HasTrackTag(IEnumerable<(string Name, string Value)>? tags)
        => tags?.Any(t =>
            string.Equals(t.Name, "Track", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Value, "true", StringComparison.OrdinalIgnoreCase)
        ) == true;

    /* ----------------------- Send ----------------------- */
    public async Task SendAsync(
        string to,
        string subject,
        string? htmlBody,
        string? textBody,
        CancellationToken ct = default,
        Dictionary<string, string>? headers = null,
        IEnumerable<(string FileName, string ContentType, byte[] Bytes)>? attachments = null,
        IEnumerable<(string Name, string Value)>? tags = null,
        string? fromOverride = null // ignored by design
    )
    {
        var from = ResolveFrom();
        var toAddr = RequireAddress(to, "To");
        var replyTo = ResolveReplyTo();
        var bccAddr = ResolveBcc();

        using var msg = new MailMessage
        {
            From = from,
            Subject = subject ?? "",
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        msg.To.Add(toAddr);

        if (replyTo != null)
            msg.ReplyToList.Add(replyTo);

        if (bccAddr != null)
            msg.Bcc.Add(bccAddr);


        // Plain text
        if (!string.IsNullOrWhiteSpace(textBody))
            msg.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(
                    textBody, Encoding.UTF8, "text/plain"));

        // HTML
        if (!string.IsNullOrWhiteSpace(htmlBody))
            msg.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(
                    htmlBody, Encoding.UTF8, "text/html"));

        // Headers
        if (headers != null)
            foreach (var kv in headers)
                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                    msg.Headers[kv.Key] = kv.Value;

        // Tags
        if (tags != null)
            foreach (var (Name, Value) in tags)
                if (!string.IsNullOrWhiteSpace(Name) && Value != null)
                    msg.Headers[$"X-PTN-{Name}"] = Value;

        // Attachments
        if (attachments != null)
        {
            foreach (var (FileName, ContentType, Bytes) in attachments)
            {
                var ms = new MemoryStream(Bytes);
                msg.Attachments.Add(
                    new Attachment(ms, ContentType) { Name = FileName });
            }
        }

        using var client = new SmtpClient(_cfg.Host, _cfg.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_cfg.User, _cfg.Pass),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        _log.LogInformation(
            "SMTP send via {Host}:{Port} From={From} ReplyTo={ReplyTo} To={To} Bcc={Bcc}",
            _cfg.Host, _cfg.Port,
            from.Address,
            replyTo?.Address ?? "<none>",
            toAddr.Address,
            bccAddr?.Address ?? "<none>"
        );

        try
        {
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(msg, ct);
        }
        catch (SmtpException ex)
        {
            _log.LogError(ex, "SMTP send failed");
            throw new InvalidOperationException(
                $"SMTP send failed: {ex.StatusCode} - {ex.Message}", ex);
        } }
    }
