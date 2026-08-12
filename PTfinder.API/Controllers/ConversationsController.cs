using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DTO.Conversations;
using PTfinder.API.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/conversations")]
    public class ConversationsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notifications;

        public ConversationsController(AppDbContext db, INotificationService notifications)
        {
            _db = db;
            _notifications = notifications;
        }

        // A client starts one private conversation with an active coach.
        [HttpPost]
        public async Task<IActionResult> Start([FromBody] StartConversationRequest request, CancellationToken ct)
        {
            var clientId = GetClientId();
            if (!clientId.HasValue) return Unauthorized(new { message = "A client session is required." });

            var body = NormalizeMessage(request?.Message);
            if (request == null || request.CoachId <= 0 || body == null)
                return BadRequest(new { message = "Choose a coach and enter a message (up to 2,000 characters)." });

            var coach = await _db.Coaches
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.CoachId && x.IsActive, ct);
            if (coach == null) return NotFound(new { message = "Coach not found." });

            var now = DateTime.UtcNow;
            var conversation = await _db.Conversations
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.CoachId == request.CoachId && x.ClientId == clientId.Value, ct);

            var isNew = conversation == null;
            if (conversation == null)
            {
                conversation = new Conversation
                {
                    CoachId = request.CoachId,
                    ClientId = clientId.Value,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    LastMessageAtUtc = now
                };
                _db.Conversations.Add(conversation);
            }

            conversation.Messages.Add(new ConversationMessage
            {
                SenderKind = ConversationSenderKind.Client,
                Body = body,
                CreatedAtUtc = now
            });
            conversation.UpdatedAtUtc = now;
            conversation.LastMessageAtUtc = now;
            conversation.CoachReadAtUtc = null;
            await _db.SaveChangesAsync(ct);

            await _notifications.NotifyCoachConversationLead(
                coach.Id,
                conversation.Id,
                isNew ? "New client lead" : "New client message",
                ct);

            return CreatedAtAction(nameof(GetForClient), new { id = conversation.Id }, new
            {
                conversation.Id,
                conversation.CreatedAtUtc,
                conversation.LastMessageAtUtc
            });
        }

        [HttpGet("client")]
        public async Task<IActionResult> ListForClient(CancellationToken ct)
        {
            var clientId = GetClientId();
            if (!clientId.HasValue) return Unauthorized(new { message = "A client session is required." });

            var conversations = await _db.Conversations
                .AsNoTracking()
                .Where(x => x.ClientId == clientId.Value)
                .OrderByDescending(x => x.LastMessageAtUtc)
                .Include(x => x.Coach)
                .Include(x => x.Messages)
                .Take(100)
                .ToListAsync(ct);

            return Ok(conversations.Select(x => ClientSummary(x)));
        }

        [HttpGet("client/{id:int}")]
        public async Task<IActionResult> GetForClient(int id, CancellationToken ct)
        {
            var clientId = GetClientId();
            if (!clientId.HasValue) return Unauthorized(new { message = "A client session is required." });

            var conversation = await _db.Conversations
                .Include(x => x.Coach)
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.Id == id && x.ClientId == clientId.Value, ct);
            if (conversation == null) return NotFound(new { message = "Conversation not found." });

            await MarkReadForClient(conversation, ct);
            return Ok(ClientDetail(conversation));
        }

        [HttpGet("coach")]
        public async Task<IActionResult> ListForCoach(CancellationToken ct)
        {
            var coachId = GetCoachId();
            if (!coachId.HasValue) return Unauthorized(new { message = "An expert session is required." });

            var coach = await _db.Coaches
                .Include(x => x.Partner)
                .FirstOrDefaultAsync(x => x.Id == coachId.Value, ct);
            if (coach == null) return Unauthorized(new { message = "Coach account not found." });

            var unlocked = HasMessagingAccess(coach);
            var conversations = await _db.Conversations
                .AsNoTracking()
                .Where(x => x.CoachId == coachId.Value)
                .OrderByDescending(x => x.LastMessageAtUtc)
                .Include(x => x.Client)
                .Include(x => x.Messages)
                .Take(100)
                .ToListAsync(ct);

            return Ok(new
            {
                isLocked = !unlocked,
                conversations = conversations.Select(x => CoachSummary(x, unlocked))
            });
        }

        [HttpGet("coach/{id:int}")]
        public async Task<IActionResult> GetForCoach(int id, CancellationToken ct)
        {
            var coachId = GetCoachId();
            if (!coachId.HasValue) return Unauthorized(new { message = "An expert session is required." });

            var coach = await _db.Coaches
                .Include(x => x.Partner)
                .FirstOrDefaultAsync(x => x.Id == coachId.Value, ct);
            if (coach == null) return Unauthorized(new { message = "Coach account not found." });
            if (!HasMessagingAccess(coach))
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new
                {
                    code = "subscription_required",
                    message = "Activate a subscription to view and reply to client leads."
                });
            }

            var conversation = await _db.Conversations
                .Include(x => x.Client)
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.Id == id && x.CoachId == coachId.Value, ct);
            if (conversation == null) return NotFound(new { message = "Conversation not found." });

            await MarkReadForCoach(conversation, ct);
            return Ok(CoachDetail(conversation));
        }

        [HttpPost("{id:int}/messages")]
        public async Task<IActionResult> Send(int id, [FromBody] SendConversationMessageRequest request, CancellationToken ct)
        {
            var body = NormalizeMessage(request?.Message);
            if (body == null) return BadRequest(new { message = "Enter a message (up to 2,000 characters)." });

            var clientId = GetClientId();
            var coachId = GetCoachId();
            if (!clientId.HasValue && !coachId.HasValue)
                return Unauthorized(new { message = "A valid client or expert session is required." });

            var conversation = await _db.Conversations
                .Include(x => x.Coach).ThenInclude(x => x.Partner)
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (conversation == null) return NotFound(new { message = "Conversation not found." });

            var sender = ConversationSenderKind.Client;
            if (clientId.HasValue && conversation.ClientId == clientId.Value)
            {
                sender = ConversationSenderKind.Client;
            }
            else if (coachId.HasValue && conversation.CoachId == coachId.Value)
            {
                if (!HasMessagingAccess(conversation.Coach))
                {
                    return StatusCode(StatusCodes.Status402PaymentRequired, new
                    {
                        code = "subscription_required",
                        message = "Activate a subscription to reply to client leads."
                    });
                }
                sender = ConversationSenderKind.Coach;
            }
            else
            {
                return Forbid();
            }

            var now = DateTime.UtcNow;
            conversation.Messages.Add(new ConversationMessage
            {
                SenderKind = sender,
                Body = body,
                CreatedAtUtc = now
            });
            conversation.UpdatedAtUtc = now;
            conversation.LastMessageAtUtc = now;
            if (sender == ConversationSenderKind.Client) conversation.CoachReadAtUtc = null;
            else conversation.ClientReadAtUtc = null;
            await _db.SaveChangesAsync(ct);

            if (sender == ConversationSenderKind.Client)
            {
                await _notifications.NotifyCoachConversationLead(
                    conversation.CoachId,
                    conversation.Id,
                    "New client message",
                    ct);
            }

            var message = conversation.Messages.OrderByDescending(x => x.Id).First();
            return Ok(new
            {
                message.Id,
                senderKind = message.SenderKind.ToString().ToLowerInvariant(),
                message.Body,
                message.CreatedAtUtc
            });
        }

        private int? GetClientId()
        {
            var value = User.FindFirst("clientId")?.Value;
            return int.TryParse(value, out var id) ? id : null;
        }

        private int? GetCoachId()
        {
            var value = User.FindFirst("coachId")?.Value;
            return int.TryParse(value, out var id) ? id : null;
        }

        private static string? NormalizeMessage(string? message)
        {
            var value = message?.Trim();
            return string.IsNullOrWhiteSpace(value) || value.Length > 2000 ? null : value;
        }

        private static bool HasMessagingAccess(Coach coach)
        {
            if (coach.PartnerId.HasValue)
                return coach.Partner?.IsActive == true;

            var now = DateTime.UtcNow;
            var hasCurrentPeriod =
                (coach.SubscriptionExpiresAtUtc.HasValue && coach.SubscriptionExpiresAtUtc > now) ||
                (coach.CurrentPeriodEndUtc.HasValue && coach.CurrentPeriodEndUtc > now);

            return coach.SubscriptionTier > SubscriptionTier.None &&
                   coach.SubscriptionStatus == SubscriptionStatus.Active &&
                   hasCurrentPeriod;
        }

        private static object ClientSummary(Conversation x) => new
        {
            x.Id,
            coach = new { x.Coach.Id, x.Coach.FullName, x.Coach.ProfileImage },
            x.LastMessageAtUtc,
            unread = x.Messages.Count(m => m.SenderKind == ConversationSenderKind.Coach &&
                (!x.ClientReadAtUtc.HasValue || m.CreatedAtUtc > x.ClientReadAtUtc.Value)),
            lastMessage = x.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => new
            {
                senderKind = m.SenderKind.ToString().ToLowerInvariant(),
                m.Body,
                m.CreatedAtUtc
            }).FirstOrDefault()
        };

        private static object ClientDetail(Conversation x) => new
        {
            x.Id,
            coach = new { x.Coach.Id, x.Coach.FullName, x.Coach.ProfileImage },
            x.CreatedAtUtc,
            x.LastMessageAtUtc,
            messages = x.Messages.OrderBy(m => m.CreatedAtUtc).Select(m => new
            {
                m.Id,
                senderKind = m.SenderKind.ToString().ToLowerInvariant(),
                m.Body,
                m.CreatedAtUtc
            })
        };

        private static object CoachSummary(Conversation x, bool unlocked) => unlocked
            ? new
            {
                x.Id,
                x.LastMessageAtUtc,
                client = new { x.Client.Id, x.Client.FullName, x.Client.PictureUrl },
                unread = x.Messages.Count(m => m.SenderKind == ConversationSenderKind.Client &&
                    (!x.CoachReadAtUtc.HasValue || m.CreatedAtUtc > x.CoachReadAtUtc.Value)),
                lastMessage = x.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => new
                {
                    senderKind = m.SenderKind.ToString().ToLowerInvariant(),
                    m.Body,
                    m.CreatedAtUtc
                }).FirstOrDefault()
            }
            : new
            {
                x.Id,
                x.LastMessageAtUtc,
                locked = true,
                title = "New client lead",
                preview = "Activate your subscription to view this lead."
            };

        private static object CoachDetail(Conversation x) => new
        {
            x.Id,
            client = new { x.Client.Id, x.Client.FullName, x.Client.PictureUrl },
            x.CreatedAtUtc,
            x.LastMessageAtUtc,
            messages = x.Messages.OrderBy(m => m.CreatedAtUtc).Select(m => new
            {
                m.Id,
                senderKind = m.SenderKind.ToString().ToLowerInvariant(),
                m.Body,
                m.CreatedAtUtc
            })
        };

        private async Task MarkReadForClient(Conversation conversation, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            conversation.ClientReadAtUtc = now;
            foreach (var message in conversation.Messages.Where(x => x.SenderKind == ConversationSenderKind.Coach && x.ReadAtUtc == null))
                message.ReadAtUtc = now;
            await _db.SaveChangesAsync(ct);
        }

        private async Task MarkReadForCoach(Conversation conversation, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            conversation.CoachReadAtUtc = now;
            foreach (var message in conversation.Messages.Where(x => x.SenderKind == ConversationSenderKind.Client && x.ReadAtUtc == null))
                message.ReadAtUtc = now;
            await _db.SaveChangesAsync(ct);
        }
    }
}
