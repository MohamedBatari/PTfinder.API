using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public NotificationsController(AppDbContext db) => _db = db;

        [HttpGet("coach/{coachId:int}")]
        public async Task<IActionResult> ForCoach(int coachId, bool unreadOnly = false)
        {
            if (GetCoachId() != coachId) return Forbid();

            var q = _db.Notifications
                .Where(n => n.RecipientKind == RecipientKind.Coach && n.CoachId == coachId);

            if (unreadOnly) q = q.Where(n => n.ReadAtUtc == null);

            var list = await q
                .OrderByDescending(n => n.Id)
                .Take(50)
                .Select(n => new
                {
                    n.Id,
                    n.Type,
                    n.Title,
                    n.Body,
                    n.Link,
                    n.CreatedAtUtc,
                    n.ReadAtUtc,
                    n.MetadataJson
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("client")]
        public async Task<IActionResult> ForClient(bool unreadOnly = false)
        {
            var clientId = GetClientId();
            if (!clientId.HasValue) return Forbid();

            var q = _db.Notifications
                .Where(n => n.RecipientKind == RecipientKind.Client && n.ClientId == clientId.Value);

            if (unreadOnly) q = q.Where(n => n.ReadAtUtc == null);

            var list = await q
                .OrderByDescending(n => n.Id)
                .Take(50)
                .Select(n => new
                {
                    n.Id,
                    n.Type,
                    n.Title,
                    n.Body,
                    n.Link,
                    n.CreatedAtUtc,
                    n.ReadAtUtc,
                    n.MetadataJson
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpPost("{id:int}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var n = await _db.Notifications.FindAsync(id);
            if (n == null) return NotFound();

            if ((n.RecipientKind == RecipientKind.Coach && n.CoachId != GetCoachId()) ||
                (n.RecipientKind == RecipientKind.Client && n.ClientId != GetClientId()))
                return Forbid();

            n.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("coach/{coachId:int}/read-all")]
        public async Task<IActionResult> MarkAllRead(int coachId)
        {
            if (GetCoachId() != coachId) return Forbid();

            var items = await _db.Notifications
                .Where(n => n.RecipientKind == RecipientKind.Coach &&
                            n.CoachId == coachId && n.ReadAtUtc == null)
                .ToListAsync();

            foreach (var n in items) n.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("client/read-all")]
        public async Task<IActionResult> MarkAllClientRead()
        {
            var clientId = GetClientId();
            if (!clientId.HasValue) return Forbid();

            var items = await _db.Notifications
                .Where(n => n.RecipientKind == RecipientKind.Client &&
                            n.ClientId == clientId.Value && n.ReadAtUtc == null)
                .ToListAsync();

            foreach (var n in items) n.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private int? GetCoachId()
        {
            var raw = User.FindFirst("coachId")?.Value ??
                      User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                      User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }

        private int? GetClientId()
        {
            var raw = User.FindFirst("clientId")?.Value ??
                      (User.IsInRole("client")
                          ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          : null) ??
                      User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }
    }
}
