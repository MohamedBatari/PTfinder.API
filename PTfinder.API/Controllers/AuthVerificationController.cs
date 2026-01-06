using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Net.Mail;
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
using PTfinder.API.Services.Emails;

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

    private static string NormalizeEmail(string? e) => (e ?? "").Trim().ToLowerInvariant();
    private string WebBaseUrl => _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";

    private string EmailLogoUrl =>
        _cfg["Email:LogoUrl"] ?? $"{WebBaseUrl.TrimEnd('/')}/images/PtFinderNow.png";

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

    private static string HashOtp(string email, string code)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"{email}:{code}");
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private string IssueEmailProofJwt(string email, int minutesValid)
    {
        var key = _cfg["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        var handler = new JwtSecurityTokenHandler();
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256
        );

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
            signingCredentials: creds
        );

        return handler.WriteToken(token);
    }

    [HttpPost("request-otp")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestVerificationDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var email = NormalizeEmail(dto.Email);
        if (string.IsNullOrWhiteSpace(email) || !MailAddress.TryCreate(email, out _))
            return BadRequest(new { error = "Invalid email." });

        var now = DateTime.UtcNow;

        var existing = await _db.EmailOtps
            .Where(x => x.Email == email && x.UsedAtUtc == null && x.ExpiresUtc > now)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (existing != null && (now - existing.LastSentUtc).TotalSeconds < 45)
            return BadRequest(new { error = "Please wait before requesting another code." });

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var minutes = dto.ExpiresMinutes <= 0 ? 10 : dto.ExpiresMinutes;

        var otp = existing ?? new EmailOtp { Email = email };
        otp.CodeHash = HashOtp(email, code);
        otp.ExpiresUtc = now.AddMinutes(minutes);
        otp.UsedAtUtc = null;
        otp.Attempts = 0;
        otp.LastSentUtc = now;

        if (existing == null) _db.EmailOtps.Add(otp);
        await _db.SaveChangesAsync(ct);

        var subject = "Your verification code — PTfinderNow";

        var html = EmailTemplates.VerifyOtpHtml(
            firstName: "there",
            code: code,
            expiresMinutes: minutes,
            logoUrl: EmailLogoUrl,
            supportEmail: "info@ptfindernow.com",
            webBaseUrl: WebBaseUrl
        );

        var text =
$@"Hi,

Use this code to verify your email with PTfinderNow:
{code}

Do not share this code. It expires in {minutes} minutes.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: email,
            subject: subject,
            htmlBody: html,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("verify-otp"),
            tags: Tags(("flow", "verify-otp")),
            fromOverride: null // ✅ always use SmtpSettings.From (no-reply)
        );

        return Ok(new { sent = true, expiresMinutes = minutes });
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto, CancellationToken ct)
    {
        var email = NormalizeEmail(dto.Email);
        var code = (dto.Code ?? "").Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code) || !MailAddress.TryCreate(email, out _))
            return BadRequest(new { error = "Email and code are required." });

        var otp = await _db.EmailOtps
            .Where(x => x.Email == email)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (otp == null || otp.UsedAtUtc != null || otp.ExpiresUtc <= DateTime.UtcNow)
            return BadRequest(new { error = "Code expired. Request a new one." });

        if (otp.Attempts >= 5)
            return BadRequest(new { error = "Too many attempts. Request a new code." });

        otp.Attempts++;
        var ok = otp.CodeHash == HashOtp(email, code);
        if (!ok)
        {
            await _db.SaveChangesAsync(ct);
            return BadRequest(new { error = "Incorrect code." });
        }

        otp.UsedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Email.ToLower() == email, ct);
        if (coach is not null && !coach.EmailVerified)
        {
            coach.EmailVerified = true;
            await _db.SaveChangesAsync(ct);
        }

        var proof = IssueEmailProofJwt(email, minutesValid: 120);
        return Ok(new { verified = true, email, emailProof = proof });
    }
}

public class VerifyOtpDto
{
    public string Email { get; set; } = "";
    public string Code { get; set; } = "";
}


