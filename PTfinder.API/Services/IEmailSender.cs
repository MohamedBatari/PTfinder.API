namespace PTfinder.API.Services;

public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string? htmlBody,
        string? textBody,
        CancellationToken ct = default,
        Dictionary<string, string>? headers = null,
        IEnumerable<(string FileName, string ContentType, byte[] Bytes)>? attachments = null,
        IEnumerable<(string Name, string Value)>? tags = null,
        string? fromOverride = null); 
}


