using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DTO.ClientContact;
using PTfinder.API.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/client-contact")]
    public class ClientContactController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ClientContactController(AppDbContext db)
        {
            _db = db;
        }

        [Authorize]
        [HttpPost("unlock/{coachId:int}")]
        public async Task<ActionResult<UnlockContactResponse>> UnlockCoachContact(
            int coachId,
            [FromBody] UnlockContactRequest req)
        {
            var clientIdClaim = User.FindFirst("clientId")?.Value
                                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!int.TryParse(clientIdClaim, out var clientId))
                return Unauthorized(new { message = "Invalid client token." });

            if (req == null || string.IsNullOrWhiteSpace(req.ActionType))
                return BadRequest(new { message = "ActionType is required." });

            var allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "unlock_whatsapp",
                "click_whatsapp",
                "unlock_email",
                "click_email",
                "unlock_phone",
                "click_phone"
            };

            var actionType = req.ActionType.Trim().ToLowerInvariant();

            if (!allowedActions.Contains(actionType))
                return BadRequest(new { message = "Invalid action type." });

            var coach = await _db.Coaches.FirstOrDefaultAsync(c => c.Id == coachId);
            if (coach == null)
                return NotFound(new { message = "Coach not found." });

            var log = new ClientContactView
            {
                ClientId = clientId,
                CoachId = coachId,
                ActionType = actionType,
                CreatedAtUtc = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                Referrer = Request.Headers.Referer.ToString(),
                ClientTimeZone = req.ClientTimeZone
            };

            _db.ClientContactViews.Add(log);
            await _db.SaveChangesAsync();

            return Ok(new UnlockContactResponse
            {
                WhatsappPhone = coach.PhoneNumber,
                Email = coach.Email,
                Phone = coach.PhoneNumber
            });
        }
    }
}
