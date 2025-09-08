using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Services;
using PTfinder.API.Settings;
using PTfinder.API.DATA.DTO;
using PTfinder.API.Helpers;
using System.Text;

namespace PTfinder.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthVerificationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly SmtpSettings _smtp;
    private readonly IConfiguration _cfg;

    public AuthVerificationController(
        AppDbContext db,
        IEmailSender sender,
        IOptions<SmtpSettings> smtp,
        IConfiguration cfg)
    {
        _db = db;
        _sender = sender;
        _smtp = smtp.Value;
        _cfg = cfg;
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string NormalizeEmail(string? e) =>
        (e ?? "").Trim().ToLowerInvariant();

    private string WebBaseUrl =>
        _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";

    private Dictionary<string, string> FlowHeaders(string flow) => new()
    {
        { "List-Unsubscribe", "<mailto:unsubscribe@ptfindernow.com>, <https://ptfindernow.com/unsubscribe>" },
        { "List-Unsubscribe-Post", "List-Unsubscribe=One-Click" },
        { "Auto-Submitted", "auto-generated" },
        { "X-Auto-Response-Suppress", "All" },
        { "Feedback-ID", $"ptn-tx:{flow}:ptfindernow" }
    };

    private static IEnumerable<(string Name, string Value)> Tags(params (string, string)[] xs)
    {
        var list = new List<(string, string)>(xs)
        {
            ("type","transactional"),
            ("channel","email"),
            ("lang","en")
        };
        return list;
    }

    /// POST /api/auth/request-verification
    /// Works for ANY email (coach may not exist yet).
    [HttpPost("request-verification")]
    public async Task<IActionResult> RequestVerification([FromBody] RequestVerificationDto dto, CancellationToken ct)
    {
        var email = NormalizeEmail(dto.Email);
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        var minutes = dto.ExpiresMinutes <= 0 ? 30 : dto.ExpiresMinutes;

        // Upsert a verification record for this email
        var ev = await _db.EmailVerifications
            .Where(v => v.Email == email && v.UsedAtUtc == null && v.ExpiresUtc > DateTime.UtcNow)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);

        if (ev is null)
        {
            ev = new EmailVerification
            {
                Email = email,
                Token = NewToken(),
                ExpiresUtc = DateTime.UtcNow.AddMinutes(minutes),
                UsedAtUtc = null
            };
            _db.EmailVerifications.Add(ev);
        }
        else
        {
            // Refresh token/expiry if there’s a still-active pending record
            ev.Token = NewToken();
            ev.ExpiresUtc = DateTime.UtcNow.AddMinutes(minutes);
        }

        await _db.SaveChangesAsync(ct);

        // Build and send the email
        var verifyUrl = $"{WebBaseUrl}/verify?token={ev.Token}";
        var subject = "Verify your email address — PTfinderNow";
        var text =
$@"Hi there,

Please verify your email address to activate your PTfinderNow account:
{verifyUrl}

This link expires in {minutes} minutes. If you didn’t start signup, you can ignore this message.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: email,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("verify-link"),
            tags: Tags(("flow", "verify-link")),
            fromOverride: _smtp.FromAddresses.Verification
        );

        // Do NOT reveal whether the email exists as a coach — avoid account enumeration.
        return Ok(new { sent = true });
    }

    /// POST /api/auth/verify-email
    /// Validates token, marks it used, returns a short-lived "email proof" JWT.
    /// If a coach already exists with this email, it’s marked verified and a "verified" email is sent.
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            return BadRequest(new { error = "Token is required" });

        var now = DateTime.UtcNow;

        var ev = await _db.EmailVerifications
            .Where(v => v.Token == dto.Token)
            .FirstOrDefaultAsync(ct);

        if (ev is null)
            return BadRequest(new { error = "Invalid token" });

        if (ev.UsedAtUtc != null)
            return BadRequest(new { error = "Token already used" });

        if (ev.ExpiresUtc <= now)
            return BadRequest(new { error = "Token expired" });

        ev.UsedAtUtc = now;
        await _db.SaveChangesAsync(ct);

        var email = ev.Email;

        // If the coach already exists → mark verified & send "verified" email
        var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Email.ToLower() == email, ct);
        if (coach is not null && !coach.EmailVerified)
        {
            coach.EmailVerified = true;
            coach.EmailVerificationToken = null;
            coach.EmailVerificationExpiresUtc = null;
            await _db.SaveChangesAsync(ct);

            var first = (coach.FullName ?? "there").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
            var dashboardUrl = $"{WebBaseUrl}/dashboard";
            var subject = "Email verified — you’re ready to accept bookings";
            var text =
$@"Hi {first},

Your email is verified and your coach account is active.

Next steps to go live:
• Complete your profile (bio, specialties, pricing, locations)
• Add a high-quality profile photo
• Set your availability so clients can request sessions

Open your dashboard:
{dashboardUrl}

If you need help at any time, just reply to this email.

{EmailText.Footer}";

            await _sender.SendAsync(
                to: email,
                subject: subject,
                htmlBody: null,
                textBody: text,
                ct: ct,
                headers: FlowHeaders("email-verified-coach"),
                tags: Tags(("flow", "email-verified-coach")),
                fromOverride: _smtp.FromAddresses.Welcome
            );
        }

        // Always return a short-lived "Email Proof" JWT so the frontend can finish signup.
        var proof = IssueEmailProofJwt(email, minutesValid: 120); // 2 hours
        return Ok(new { verified = true, email, emailProof = proof });
    }

    private string IssueEmailProofJwt(string email, int minutesValid)
    {
        var key = _cfg["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Jwt:Key is not configured.");

        var handler = new JwtSecurityTokenHandler();
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim("scope", "email-verified"),
            new Claim("type", "email-proof")
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(minutesValid),
            signingCredentials: creds);

        return handler.WriteToken(token);
    }
}


