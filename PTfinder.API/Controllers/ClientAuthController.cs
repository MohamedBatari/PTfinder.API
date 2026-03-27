using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.Models;
using PTfinder.API.Services;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/client-auth")]
    public class ClientAuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IClientJwtService _jwtService;
        private readonly IConfiguration _config;

        public ClientAuthController(
            AppDbContext db,
            IClientJwtService jwtService,
            IConfiguration config)
        {
            _db = db;
            _jwtService = jwtService;
            _config = config;
        }

        [AllowAnonymous]
        [HttpPost("google")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> GoogleLogin([FromForm] string credential)
        {
            if (string.IsNullOrWhiteSpace(credential))
                return BadRequest(new { message = "Google credential is required." });

            GoogleJsonWebSignature.Payload payload;
            try
            {
                var googleClientId = _config["GoogleAuth:ClientId"];
                if (string.IsNullOrWhiteSpace(googleClientId))
                    return StatusCode(500, new { message = "GoogleAuth:ClientId is not configured." });

                payload = await GoogleJsonWebSignature.ValidateAsync(
                    credential,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { googleClientId }
                    });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = "Invalid Google token.", detail = ex.Message });
            }

            if (string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Subject))
                return Unauthorized(new { message = "Invalid Google account data." });

            var email = payload.Email.Trim().ToLowerInvariant();
            var googleSub = payload.Subject;
            var now = DateTime.UtcNow;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers.UserAgent.ToString();

            var client = await _db.Clients
                .FirstOrDefaultAsync(x => x.GoogleSub == googleSub || x.Email == email);

            if (client == null)
            {
                client = new Client
                {
                    GoogleSub = googleSub,
                    Email = email,
                    FullName = payload.Name?.Trim() ?? "Client",
                    PictureUrl = payload.Picture,
                    EmailVerified = payload.EmailVerified,
                    CreatedAtUtc = now,
                    LastLoginAtUtc = now,

                    TermsAccepted = true,
                    TermsVersion = "v1",
                    TermsAcceptedAtUtc = now,

                    PrivacyAccepted = true,
                    PrivacyVersion = "v1",
                    PrivacyAcceptedAtUtc = now,

                    LastIpAddress = ip,
                    LastUserAgent = ua,
                    ClientTimeZone = "Asia/Dubai"
                };

                _db.Clients.Add(client);
            }
            else
            {
                client.GoogleSub = googleSub;
                client.Email = email;
                client.FullName = payload.Name?.Trim() ?? client.FullName;
                client.PictureUrl = payload.Picture ?? client.PictureUrl;
                client.EmailVerified = payload.EmailVerified;
                client.LastLoginAtUtc = now;
                client.LastIpAddress = ip;
                client.LastUserAgent = ua;

                client.TermsAccepted = true;
                client.TermsVersion = client.TermsVersion ?? "v1";
                client.TermsAcceptedAtUtc ??= now;

                client.PrivacyAccepted = true;
                client.PrivacyVersion = client.PrivacyVersion ?? "v1";
                client.PrivacyAcceptedAtUtc ??= now;

                client.ClientTimeZone ??= "Asia/Dubai";
            }

            await _db.SaveChangesAsync();

            var token = _jwtService.GenerateToken(client);

            var frontendBase =
                _config["FrontendBase"]?.TrimEnd('/')
                ?? "https://www.ptfindernow.com";

            var redirectUrl =
                $"{frontendBase}/google-auth-success" +
                $"?token={Uri.EscapeDataString(token)}" +
                $"&name={Uri.EscapeDataString(client.FullName ?? "")}" +
                $"&email={Uri.EscapeDataString(client.Email ?? "")}" +
                $"&picture={Uri.EscapeDataString(client.PictureUrl ?? "")}";

            return Redirect(redirectUrl);
        }
    }
}