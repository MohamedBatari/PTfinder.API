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

    private static string ExtractEmail(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        var m = Regex.Match(s, @"<\s*([^>\s]+@[^>\s]+)\s*>");
        if (m.Success) s = m.Groups[1].Value; // support "Name <user@domain>"
        return s;
    }

    private static MailAddress RequireAddress(string raw, string label)
    {
        var email = ExtractEmail(raw);
        if (!string.IsNullOrWhiteSpace(email) && MailAddress.TryCreate(email, out var addr))
            return addr;
        throw new FormatException($"Invalid {label} email: '{raw ?? "<null>"}'");
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
        // Resolve From in priority order
        var fromRaw = !string.IsNullOrWhiteSpace(fromOverride)
            ? fromOverride
            : _cfg.FromAddresses?.Default;

        var from = RequireAddress(fromRaw, "From");
        var toAddr = RequireAddress(to, "To");

        using var msg = new MailMessage { From = from, Subject = subject ?? "", BodyEncoding = Encoding.UTF8 };
        msg.To.Add(toAddr);

        if (!string.IsNullOrWhiteSpace(_cfg.ReplyTo))
            msg.ReplyToList.Add(RequireAddress(_cfg.ReplyTo, "ReplyTo"));

        if (!string.IsNullOrWhiteSpace(textBody))
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(textBody, Encoding.UTF8, "text/plain"));
        if (!string.IsNullOrWhiteSpace(htmlBody))
            msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html"));

        if (headers != null) foreach (var kv in headers) msg.Headers[kv.Key] = kv.Value;
        if (tags != null) foreach (var (Name, Value) in tags) msg.Headers[$"X-PTN-{Name}"] = Value;

        if (attachments != null)
            foreach (var (FileName, ContentType, Bytes) in attachments)
                msg.Attachments.Add(new Attachment(new MemoryStream(Bytes), ContentType) { Name = FileName });

        using var client = new SmtpClient(_cfg.Host, _cfg.Port)
        {
            EnableSsl = true, // SMTP2GO supports TLS on 2525/587
            Credentials = new NetworkCredential(_cfg.User, _cfg.Pass),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        _log.LogInformation("SMTP send via {Host}:{Port} From={From} To={To}", _cfg.Host, _cfg.Port, from.Address, toAddr.Address);

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

