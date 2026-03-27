using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA;
using PTfinder.API.DTO.ClientAuth;
using PTfinder.API.DTO.ClientAuth;
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

        [HttpPost("google")]
        public async Task<ActionResult<GoogleClientLoginResponse>> GoogleLogin([FromBody] GoogleClientLoginRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.IdToken))
                return BadRequest(new { message = "Google token is required." });

            if (!req.TermsAccepted || !req.PrivacyAccepted)
                return BadRequest(new { message = "Terms and Privacy must be accepted." });

            GoogleJsonWebSignature.Payload payload;
            try
            {
                var googleClientId = _config["GoogleAuth:ClientId"];
                if (string.IsNullOrWhiteSpace(googleClientId))
                    return StatusCode(500, new { message = "GoogleAuth:ClientId is not configured." });

                payload = await GoogleJsonWebSignature.ValidateAsync(
                    req.IdToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = new[] { googleClientId }
                    });
            }
            catch
            {
                return Unauthorized(new { message = "Invalid Google token." });
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
                    TermsVersion = req.TermsVersion ?? "v1",
                    TermsAcceptedAtUtc = now,
                    PrivacyAccepted = true,
                    PrivacyVersion = req.PrivacyVersion ?? "v1",
                    PrivacyAcceptedAtUtc = now,
                    LastIpAddress = ip,
                    LastUserAgent = ua,
                    ClientTimeZone = req.ClientTimeZone
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
                client.ClientTimeZone = req.ClientTimeZone;

                client.TermsAccepted = true;
                client.TermsVersion = req.TermsVersion ?? client.TermsVersion ?? "v1";
                client.TermsAcceptedAtUtc ??= now;

                client.PrivacyAccepted = true;
                client.PrivacyVersion = req.PrivacyVersion ?? client.PrivacyVersion ?? "v1";
                client.PrivacyAcceptedAtUtc ??= now;
            }

            await _db.SaveChangesAsync();

            var token = _jwtService.GenerateToken(client);

            return Ok(new GoogleClientLoginResponse
            {
                Token = token,
                Client = new ClientAuthUserDto
                {
                    Id = client.Id,
                    FullName = client.FullName,
                    Email = client.Email,
                    PictureUrl = client.PictureUrl
                }
            });
        }
    }
}
