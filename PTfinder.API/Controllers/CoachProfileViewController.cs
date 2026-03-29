using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO.ClientAuth;
using PTfinder.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/coach-profile-view")]
    public class CoachProfileViewController : ControllerBase
    {
        private readonly AppDbContext _db;

        public CoachProfileViewController(AppDbContext db)
        {
            _db = db;
        }

        private int? TryGetClientId()
        {
            var clientIdClaim =
                User.FindFirst("clientId")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (int.TryParse(clientIdClaim, out var clientId))
                return clientId;

            return null;
        }

        [AllowAnonymous]
        [HttpPost("track/{coachId:int}")]
        public async Task<IActionResult> TrackProfileView(
            int coachId,
            [FromBody] TrackCoachProfileViewRequest? req)
        {
            var coachExists = await _db.Coaches.AnyAsync(c => c.Id == coachId);
            if (!coachExists)
                return NotFound(new { message = "Coach not found." });

            var clientId = User?.Identity?.IsAuthenticated == true
                ? TryGetClientId()
                : null;

            var sessionId = req?.SessionId?.Trim();
            var viewSource = string.IsNullOrWhiteSpace(req?.ViewSource)
                ? "coach_page"
                : req!.ViewSource!.Trim().ToLowerInvariant();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers.UserAgent.ToString();
            var referrer = Request.Headers.Referer.ToString();
            var tz = req?.ClientTimeZone;

            // optional dedupe window: 30 minutes
            var windowStartUtc = DateTime.UtcNow.AddMinutes(-30);

            bool alreadyTracked = false;

            if (clientId.HasValue)
            {
                alreadyTracked = await _db.CoachProfileViews.AnyAsync(x =>
                    x.CoachId == coachId &&
                    x.ClientId == clientId &&
                    x.CreatedAtUtc >= windowStartUtc);
            }
            else if (!string.IsNullOrWhiteSpace(sessionId))
            {
                alreadyTracked = await _db.CoachProfileViews.AnyAsync(x =>
                    x.CoachId == coachId &&
                    x.ClientId == null &&
                    x.SessionId == sessionId &&
                    x.CreatedAtUtc >= windowStartUtc);
            }

            if (!alreadyTracked)
            {
                var log = new CoachProfileView
                {
                    CoachId = coachId,
                    ClientId = clientId,
                    SessionId = sessionId,
                    ViewSource = viewSource,
                    Referrer = referrer,
                    ClientTimeZone = tz,
                    IpAddress = ip,
                    UserAgent = ua,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.CoachProfileViews.Add(log);
                await _db.SaveChangesAsync();
            }

            return Ok(new { success = true });
        }

        [Authorize]
        [HttpGet("summary/{coachId:int}")]
        public async Task<ActionResult<CoachAnalyticsSummaryResponse>> GetCoachSummary(int coachId)
        {
            var coachExists = await _db.Coaches.AnyAsync(c => c.Id == coachId);
            if (!coachExists)
                return NotFound(new { message = "Coach not found." });

            var totalProfileViews = await _db.CoachProfileViews
                .CountAsync(x => x.CoachId == coachId);

            var signedInProfileViews = await _db.CoachProfileViews
                .CountAsync(x => x.CoachId == coachId && x.ClientId != null);

            var anonymousProfileViews = await _db.CoachProfileViews
                .CountAsync(x => x.CoachId == coachId && x.ClientId == null);

            var signedInUnique = await _db.CoachProfileViews
                .Where(x => x.CoachId == coachId && x.ClientId != null)
                .Select(x => x.ClientId)
                .Distinct()
                .CountAsync();

            var anonymousUnique = await _db.CoachProfileViews
                .Where(x => x.CoachId == coachId && x.ClientId == null && x.SessionId != null)
                .Select(x => x.SessionId)
                .Distinct()
                .CountAsync();

            var whatsappClicks = await _db.ClientContactViews
                .CountAsync(x => x.CoachId == coachId && x.ActionType == "click_whatsapp");

            var emailClicks = await _db.ClientContactViews
                .CountAsync(x => x.CoachId == coachId && x.ActionType == "click_email");

            var phoneClicks = await _db.ClientContactViews
                .CountAsync(x => x.CoachId == coachId && x.ActionType == "click_phone");

            return Ok(new CoachAnalyticsSummaryResponse
            {
                CoachId = coachId,
                TotalProfileViews = totalProfileViews,
                UniqueVisitors = signedInUnique + anonymousUnique,
                SignedInProfileViews = signedInProfileViews,
                AnonymousProfileViews = anonymousProfileViews,
                WhatsappClicks = whatsappClicks,
                EmailClicks = emailClicks,
                PhoneClicks = phoneClicks
            });
        }

        [Authorize]
        [HttpGet("recent-visitors/{coachId:int}")]
        public async Task<ActionResult<List<RecentProfileVisitorDto>>> GetRecentVisitors(int coachId)
        {
            var coachExists = await _db.Coaches.AnyAsync(c => c.Id == coachId);
            if (!coachExists)
                return NotFound(new { message = "Coach not found." });

            var data = await _db.CoachProfileViews
                .Where(x => x.CoachId == coachId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(50)
                .Select(x => new RecentProfileVisitorDto
                {
                    ClientId = x.ClientId,
                    VisitorLabel = x.ClientId != null
                        ? $"Client #{x.ClientId}"
                        : "Anonymous visitor",
                    SessionId = x.SessionId,
                    ViewedAtUtc = x.CreatedAtUtc,
                    ViewSource = x.ViewSource,
                    ClientTimeZone = x.ClientTimeZone
                })
                .ToListAsync();

            return Ok(data);
        }

        [Authorize]
        [HttpGet("recent-contacts/{coachId:int}")]
        public async Task<ActionResult<List<RecentContactActionDto>>> GetRecentContacts(int coachId)
        {
            var coachExists = await _db.Coaches.AnyAsync(c => c.Id == coachId);
            if (!coachExists)
                return NotFound(new { message = "Coach not found." });

            var data = await _db.ClientContactViews
                .Where(x => x.CoachId == coachId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(50)
                .Select(x => new RecentContactActionDto
                {
                    ClientId = x.ClientId,
                    VisitorLabel = $"Client #{x.ClientId}",
                    ActionType = x.ActionType,
                    CreatedAtUtc = x.CreatedAtUtc,
                    ClientTimeZone = x.ClientTimeZone
                })
                .ToListAsync();

            return Ok(data);
        }
    }
}
