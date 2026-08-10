using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.Helpers;

namespace PTfinder.API.Services.Emails;

public interface IPasswordResetEmailJob
{
    Task SendIfAccountExistsAsync(
        string email,
        CancellationToken cancellationToken);
}

public sealed class PasswordResetEmailJob : IPasswordResetEmailJob
{
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _db;
    private readonly IPasswordResetTokenService _tokens;

    public PasswordResetEmailJob(
        IEmailSender emailSender,
        IConfiguration configuration,
        AppDbContext db,
        IPasswordResetTokenService tokens)
    {
        _emailSender = emailSender;
        _configuration = configuration;
        _db = db;
        _tokens = tokens;
    }

    public async Task SendIfAccountExistsAsync(
        string email,
        CancellationToken cancellationToken)
    {
        const int expiresMinutes = 30;

        var coach = await _db.Coaches
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);

        if (coach is null)
            return;

        var webBaseUrl =
            (_configuration["Web:BaseUrl"] ?? "https://ptfindernow.com").TrimEnd('/');
        var logoUrl =
            _configuration["Email:LogoUrl"] ?? $"{webBaseUrl}/images/PtFinderNow.png";
        var firstName = coach.FullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "there";
        var resetToken = _tokens.Issue(coach, expiresMinutes);
        var resetUrl =
            $"{webBaseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";

        var html = EmailTemplates.PasswordResetHtml(
            firstName,
            resetUrl,
            expiresMinutes,
            logoUrl,
            webBaseUrl: webBaseUrl);
        var text = $@"Hi {firstName},

We received a request to reset your PTfinderNow password.

Reset your password:
{resetUrl}

This link expires in {expiresMinutes} minutes and stops working after your password is changed.
If you did not request this reset, you can safely ignore this email.

{EmailText.Footer}";

        await _emailSender.SendAsync(
            to: email,
            subject: "Reset your password - PTfinderNow",
            htmlBody: html,
            textBody: text,
            ct: cancellationToken,
            headers: FlowHeaders("password-reset"),
            tags: FlowTags("password-reset"),
            fromOverride: null);
    }

    private static Dictionary<string, string> FlowHeaders(string flow) => new()
    {
        { "Auto-Submitted", "auto-generated" },
        { "X-Auto-Response-Suppress", "All" },
        { "Feedback-ID", $"ptn-tx:{flow}:ptfindernow" }
    };

    private static IEnumerable<(string Name, string Value)> FlowTags(string flow) => new[]
    {
        ("type", "transactional"),
        ("channel", "email"),
        ("flow", flow)
    };
}
