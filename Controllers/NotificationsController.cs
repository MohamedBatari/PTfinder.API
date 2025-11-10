using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public NotificationsController(AppDbContext db) => _db = db;

        // GET api/notifications/coach/12?unreadOnly=true
        [HttpGet("coach/{coachId:int}")]
        public async Task<IActionResult> ForCoach(int coachId, bool unreadOnly = false)
        {
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
            n.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("coach/{coachId:int}/read-all")]
        public async Task<IActionResult> MarkAllRead(int coachId)
        {
            var items = await _db.Notifications
                .Where(n => n.RecipientKind == RecipientKind.Coach && n.CoachId == coachId && n.ReadAtUtc == null)
                .ToListAsync();

            foreach (var n in items) n.ReadAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}

