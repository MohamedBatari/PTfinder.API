using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.Services;
using PTfinder.API.Services.Emails;

namespace PTfinder.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
[EnableRateLimiting("password-reset")]
public sealed class PasswordResetController : ControllerBase
{
    private const string GenericForgotPasswordMessage =
        "If an account exists for that email, a password reset link has been sent.";

    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly IMemoryCache _cache;
    private readonly IPasswordResetTokenService _tokens;

    public PasswordResetController(
        AppDbContext db,
        IBackgroundJobClient jobs,
        IMemoryCache cache,
        IPasswordResetTokenService tokens)
    {
        _db = db;
        _jobs = jobs;
        _cache = cache;
        _tokens = tokens;
    }

    [HttpPost("forgot-password")]
    public IActionResult ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Enter a valid email address." });

        var email = NormalizeEmail(request.Email);
        if (!MailAddress.TryCreate(email, out _))
            return BadRequest(new { message = "Enter a valid email address." });

        var cooldownKey = BuildCooldownKey(email);
        if (_cache.TryGetValue(cooldownKey, out _))
            return Ok(new { message = GenericForgotPasswordMessage });

        _cache.Set(cooldownKey, true, TimeSpan.FromSeconds(60));

        _jobs.Enqueue<IPasswordResetEmailJob>(job =>
            job.SendIfAccountExistsAsync(
                email,
                CancellationToken.None));

        return Ok(new { message = GenericForgotPasswordMessage });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "A valid reset token and new password are required." });

        var passwordError = ValidatePassword(request.NewPassword);
        if (passwordError is not null)
            return BadRequest(new { message = passwordError });

        var tokenData = _tokens.Validate(request.Token);
        if (tokenData is null)
            return BadRequest(new { message = "This reset link is invalid or has expired." });

        var coach = await _db.Coaches
            .SingleOrDefaultAsync(candidate => candidate.Id == tokenData.CoachId, cancellationToken);

        if (coach is null || !_tokens.MatchesCurrentPassword(tokenData.PasswordFingerprint, coach.Password))
            return BadRequest(new { message = "This reset link is invalid or has expired." });

        coach.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        coach.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Your password has been reset successfully." });
    }

    private static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string BuildCooldownKey(string email) =>
        $"password-reset:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)))}";

    private static string? ValidatePassword(string password)
    {
        if (password.Length < 8)
            return "Password must contain at least 8 characters.";
        if (password.Length > 128)
            return "Password cannot exceed 128 characters.";
        if (!password.Any(char.IsUpper))
            return "Password must include an uppercase letter.";
        if (!password.Any(char.IsDigit))
            return "Password must include a number.";
        return null;
    }
}
