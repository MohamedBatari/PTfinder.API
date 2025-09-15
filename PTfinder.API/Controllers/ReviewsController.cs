using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReviewsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _cfg;

        public ReviewsController(AppDbContext context, IConfiguration cfg)
        {
            _context = context;
            _cfg = cfg;
        }

        // GET: api/Reviews/coach/123
        [HttpGet("coach/{coachId:int}")]
        public async Task<IActionResult> GetReviewsForCoach(int coachId, CancellationToken ct)
        {
            var reviews = await _context.Reviews
                .Where(r => r.CoachId == coachId)
                .OrderByDescending(r => r.CreatedAt) // newest first
                .ToListAsync(ct);

            var dtos = reviews.Select(r => new ReviewDto
            {
                Id = r.Id,
                StudentName = r.StudentName,
                StudentEmail = r.StudentEmail,
                Comment = r.Comment,
                Rating = r.Rating,
                GoogleVerified = r.GoogleVerified,
                Avatar = r.AvatarUrl,
                CreatedAt = r.CreatedAt
            });

            return Ok(dtos);
        }

        // POST: api/Reviews
        [HttpPost]
        public async Task<IActionResult> AddReview([FromBody] ReviewCreateDto reviewDto, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Clamp rating 1..5
            var rating = Math.Clamp(reviewDto.Rating, 1, 5);

            // Validate Google ID token against allowed client IDs in configuration
            var clientIds = _cfg.GetSection("GoogleAuth:ClientIds").Get<string[]>() ?? Array.Empty<string>();
            if (clientIds.Length == 0)
                return StatusCode(500, "GoogleAuth:ClientIds is not configured on the server.");

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(
                    reviewDto.GoogleIdToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = clientIds
                    });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = "Invalid Google token.", detail = ex.Message });
            }

            if (payload == null || !payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
                return Unauthorized("Google account email is not verified.");

            var email = payload.Email.Trim().ToLowerInvariant();

            // Ensure coach exists
            var coach = await _context.Coaches
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == reviewDto.CoachId, ct);

            if (coach == null)
                return NotFound("PT not found.");

            // Block self-review (coach email == reviewer email)
            if (!string.IsNullOrWhiteSpace(coach.Email) &&
                string.Equals(coach.Email.Trim(), email, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("You cannot review your own PT profile.");
            }

            // One review per Google email per PT
            var already = await _context.Reviews
                .AsNoTracking()
                .AnyAsync(r => r.CoachId == reviewDto.CoachId &&
                               r.StudentEmail != null &&
                               r.StudentEmail.ToLower() == email, ct);

            if (already)
                return Conflict("You have already reviewed this PT.");

            // Create and save
            var review = new Review
            {
                CoachId = reviewDto.CoachId,
                StudentName = string.IsNullOrWhiteSpace(payload.Name)
                    ? email.Split('@')[0]
                    : payload.Name,
                StudentEmail = email,
                Comment = (reviewDto.Comment ?? string.Empty).Trim(),
                Rating = rating,
                GoogleSub = payload.Subject,
                AvatarUrl = payload.Picture,
                GoogleVerified = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync(ct);

            var response = new ReviewDto
            {
                Id = review.Id,
                StudentName = review.StudentName,
                StudentEmail = review.StudentEmail,
                Comment = review.Comment,
                Rating = review.Rating,
                GoogleVerified = review.GoogleVerified,
                Avatar = review.AvatarUrl,
                CreatedAt = review.CreatedAt
            };

            return CreatedAtAction(nameof(GetReviewsForCoach),
                new { coachId = review.CoachId }, response);
        }
    }
}
