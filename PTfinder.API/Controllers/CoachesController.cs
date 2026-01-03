using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Helpers;
using PTfinder.API.Services;
using PTfinder.API.Settings;
using System.IO;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoachesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BlobStorageService _blobs;
        private readonly IEmailSender _sender;
        private readonly SmtpSettings _smtp;
        private readonly ILogger<CoachesController> _logger;


        public CoachesController(
            AppDbContext context,
            BlobStorageService blobs,
            IEmailSender sender,
            IOptions<SmtpSettings> smtp, ILogger<CoachesController> logger)
        {
            _context = context;
            _blobs = blobs;
            _sender = sender;
            _smtp = smtp.Value;
            _logger = logger;

        }

        private Dictionary<string, string> FlowHeaders(string flow) => new()
        {
            { "List-Unsubscribe", "<mailto:unsubscribe@ptfindernow.com>, <https://ptfindernow.com/unsubscribe>" },
            { "List-Unsubscribe-Post", "List-Unsubscribe=One-Click" },
            { "Auto-Submitted", "auto-generated" },
            { "X-Auto-Response-Suppress", "All" },
            { "Feedback-ID", $"ptn-tx:{flow}:ptfindernow" }
        };

        private string? SafeFrom(string? preferredFrom)
        {
            var chosen = string.IsNullOrWhiteSpace(preferredFrom) ? _smtp?.FromAddresses?.Default : preferredFrom;
            return string.IsNullOrWhiteSpace(chosen) ? null : chosen;
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetCoaches()
        {
            var coaches = await _context.Coaches
                .Include(c => c.Category)
                .Include(c => c.Speciality)
                .Include(c => c.Country)
                .Include(c => c.City)
                .Include(c => c.Area)
                .Include(c => c.Availabilities)
                .Include(c => c.Reviews)
                .Include(c => c.GalleryMedia)
                .Include(c => c.Partner)
                .ToListAsync();

            var response = coaches.Select(coach => new
            {
                coach.Id,
                coach.FullName,
                coach.Email,
                coach.PhoneNumber,
                coach.Gender,
                coach.Price,
                coach.Description,

                // Location Data
                Country = coach.Country?.Name,
                City = coach.City?.Name,
                Area = coach.Area?.Name,

                // Category / Specialty
                Category = coach.Category?.Name,
                Speciality = new { coach.Speciality?.Id, coach.Speciality?.Name },

                // Profile Image (with SAS link)
                ProfileImage = string.IsNullOrWhiteSpace(coach.ProfileImage)
                    ? null
                    : _blobs.GetReadUrl(coach.ProfileImage, TimeSpan.FromMinutes(60)),

                // Availability
                Availabilities = coach.Availabilities.Select(a => new
                {
                    a.Id,
                    AvailableDate = a.AvailableDate.ToString("yyyy-MM-dd"),
                    a.TimeSlot
                }),


                // Subscription Info
                coach.SubscriptionTier,
                coach.SubscriptionStatus,
                coach.SubscriptionStartedAtUtc,
                coach.SubscriptionExpiresAtUtc,
                coach.CurrentPeriodEndUtc,
                coach.StripeCustomerId,
                coach.StripeSubscriptionId,

                // Freelancer or Company Coach
       

                // Flags
                coach.EmailVerified,
                coach.IsActive,
                coach.CreatedAtUtc,
                coach.UpdatedAtUtc
            });

            return Ok(response);
        }



        [HttpGet("{id}")]
        public async Task<IActionResult> GetCoach(int id)
        {
            try
            {
                var coach = await _context.Coaches
                    .Include(c => c.Category)
                    .Include(c => c.Speciality)
                    .Include(c => c.Country)
                    .Include(c => c.City)
                    .Include(c => c.Area)
                    .Include(c => c.Availabilities)
                    .Include(c => c.Reviews)
                    .Include(c => c.GalleryMedia)
                    .Include(c => c.Partner)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (coach == null)
                    return NotFound();

                // build SAS URL for profile image (same style as Search)
                var profileImageUrl = string.IsNullOrWhiteSpace(coach.ProfileImage)
                    ? null
                    : _blobs.GetReadUrl(coach.ProfileImage, TimeSpan.FromMinutes(60));

                var response = new
                {
                    coach.Id,
                    coach.FullName,
                    coach.Email,
                    coach.PhoneNumber,
                    coach.Gender,
                    coach.Price,
                    coach.Description,

                    // IDs (for Settings)
                    coach.CountryId,
                    coach.CityId,
                    coach.AreaId,
                    coach.CategoryId,
                    coach.SpecialityId,

                    // Names (for display)
                    Country = coach.Country?.Name,
                    City = coach.City?.Name,
                    Area = coach.Area?.Name,
                    Category = coach.Category?.Name,
                    Speciality = new
                    {
                        Id = coach.Speciality?.Id,
                        Name = coach.Speciality?.Name
                    },

                    // ✅ full URL, ready for <img src="...">
                    ProfileImage = profileImageUrl,

                    Availabilities = coach.Availabilities.Select(a => new
                    {
                        a.Id,
                        AvailableDate = a.AvailableDate.ToString("yyyy-MM-dd"),
                        a.TimeSlot
                    }),

                    // ===== SUBSCRIPTION FIELDS =====
                    coach.SubscriptionTier,
                    coach.SubscriptionStatus,
                    coach.SubscriptionStartedAtUtc,
                    coach.SubscriptionExpiresAtUtc,
                    coach.CurrentPeriodEndUtc,
                    coach.StripeCustomerId,
                    coach.StripeSubscriptionId,

                    // ===== STRIPE STATUS (NEW) =====
                    StripeAccountId = coach.StripeAccountId,
                    StripeChargesEnabled = coach.StripeChargesEnabled,
                    StripePayoutsEnabled = coach.StripePayoutsEnabled,
                    StripeDetailsSubmitted = coach.StripeDetailsSubmitted,

                    coach.EmailVerified,
                    coach.IsActive,
                    coach.CreatedAtUtc,
                    coach.UpdatedAtUtc
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetCoach({Id})", id);
                return StatusCode(500, new { message = "Error in GetCoach", error = ex.Message });
            }
        }
   





        // ─────────────────────────────────────────────────────────────────────────
        // GET: api/coaches/check-email?email=...
        // ─────────────────────────────────────────────────────────────────────────
        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmailExists(string email)
        {
            var coachExists = await _context.Coaches.AnyAsync(c => c.Email == email);
            return Ok(new { exists = coachExists });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GET: api/coaches/Search
        // Accepts: Specialty / Speciality, Country, City, Area, Gender
        // Optional: CategoryId, SpecialityId
        // Example:
        // /api/coaches/Search?Specialty=Personal+Training&Country=United+Arab+Emirates&City=Dubai&Area=Dubai+Marina&Gender=Male
        // ─────────────────────────────────────────────────────────────────────────
        [HttpGet("Search")]
        public async Task<IActionResult> Search(
         [FromQuery] int? CategoryId,
         [FromQuery] int? SpecialityId,
         [FromQuery] int? CountryId,
         [FromQuery] int? CityId,
         [FromQuery] int? AreaId,
         [FromQuery] string? Gender,
         [FromQuery] string? FullName)
        {
            var nowUtc = DateTime.UtcNow;

            var query = _context.Coaches
                .Include(c => c.Category)
                .Include(c => c.Speciality)
                .Include(c => c.Country)
                .Include(c => c.City)
                .Include(c => c.Area)
                .AsQueryable();

            if (CategoryId.HasValue)
                query = query.Where(c => c.CategoryId == CategoryId.Value);

            if (SpecialityId.HasValue)
                query = query.Where(c => c.SpecialityId == SpecialityId.Value);

            if (CountryId.HasValue)
                query = query.Where(c => c.CountryId == CountryId.Value);

            if (CityId.HasValue)
                query = query.Where(c => c.CityId == CityId.Value);

            if (AreaId.HasValue)
                query = query.Where(c => c.AreaId == AreaId.Value);

            if (!string.IsNullOrWhiteSpace(Gender))
            {
                var g = Gender.Trim().ToLower();
                query = query.Where(c =>
                    !string.IsNullOrEmpty(c.Gender) &&
                    c.Gender.ToLower() == g);
            }

            if (!string.IsNullOrWhiteSpace(FullName))
            {
                var f = FullName.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().Contains(f));
            }

            // 🔥 ONLY active subscriptions "till date"
            query = query.Where(c =>
                c.IsActive &&                           // coach active
                c.EmailVerified &&                      // email verified (optional but recommended)
                c.SubscriptionTier > 0 &&               // has a paid/active tier
                (
                    (c.SubscriptionExpiresAtUtc.HasValue && c.SubscriptionExpiresAtUtc > nowUtc) ||
                    (c.CurrentPeriodEndUtc.HasValue && c.CurrentPeriodEndUtc > nowUtc)
                )
            );

            var result = await query
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    ProfileImage = string.IsNullOrWhiteSpace(c.ProfileImage)
                        ? null
                        : _blobs.GetReadUrl(c.ProfileImage, TimeSpan.FromMinutes(60)),
                    c.Price,
                    c.Description,

                    CategoryId = c.CategoryId,
                    SpecialityId = c.SpecialityId,
                    CountryId = c.CountryId,
                    CityId = c.CityId,
                    AreaId = c.AreaId,

                    CategoryName = c.Category != null ? c.Category.Name : null,
                    SpecialtyName = c.Speciality != null ? c.Speciality.Name : null,
                    CountryName = c.Country != null ? c.Country.Name : null,
                    CityName = c.City != null ? c.City.Name : null,
                    AreaName = c.Area != null ? c.Area.Name : null,

                    // 👇 expose subscription info to frontend (if you want)
                    c.SubscriptionTier,
                    c.SubscriptionStatus,
                    c.SubscriptionExpiresAtUtc,
                    c.CurrentPeriodEndUtc
                })
                .ToListAsync();

            return Ok(result);
        }

        // GET /api/Coaches/Names?q=mo   -> suggestions that start with "mo"
        // If q is missing/empty, returns all coach names (you may cap the count if large)
        [HttpGet("Names")]
        public async Task<IActionResult> Names([FromQuery] string? q, [FromQuery] int take = 1000)
        {
            var query = _context.Coaches.AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().StartsWith(t));
            }

            // Order for stable UX
            var result = await query
                .OrderBy(c => c.FullName)
                .Select(c => new { c.Id, c.FullName })
                .Take(take)
                .ToListAsync();

            return Ok(result);
        }


        // ─────────────────────────────────────────────────────────────────────────
        // POST: api/coaches (multipart/form-data)
        // ─────────────────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<ActionResult<Coach>> PostCoach([FromForm] CoachCreateDto dto)
        {
            string? blobName = null;

            if (dto.ProfileImage != null && dto.ProfileImage.Length > 0)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);
                await using var stream = dto.ProfileImage.OpenReadStream();
                await _blobs.UploadAsync(fileName, stream, dto.ProfileImage.ContentType);
                blobName = fileName;
            }

            var now = DateTime.UtcNow;
            var end = now.AddMonths(12); // ✅ change to 6 if you want

            var coach = new Coach
            {
                FullName = dto.FullName?.Trim(),
                Email = dto.Email?.Trim().ToLower(),
                PhoneNumber = dto.PhoneNumber,

                // ✅ hash password
                Password = BCrypt.Net.BCrypt.HashPassword(dto.Password),

                Gender = dto.Gender,
                Price = dto.Price,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                SpecialityId = dto.SpecialityId,
                CountryId = dto.CountryId,
                CityId = dto.CityId,
                AreaId = dto.AreaId,
                ProfileImage = blobName,

                // ✅ Verified via OTP in your flow
                EmailVerified = true,
                IsActive = true,

                // ✅ PRELAUNCH: Auto Premium (ENUMS)
                SubscriptionTier = (SubscriptionTier)1,        // Premium
                SubscriptionStatus = (SubscriptionStatus)2,    // Active
                SubscriptionStartedAtUtc = now,
                SubscriptionExpiresAtUtc = end,
                CurrentPeriodEndUtc = end,

                // ✅ Stripe not used in prelaunch
                StripeCustomerId = null,
                StripeSubscriptionId = null,

                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _context.Coaches.Add(coach);
            await _context.SaveChangesAsync();

            // Send Welcome email (coach)
            var first = (coach.FullName ?? "there")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "there";

            var subject = $"Welcome to PTfinderNow, {first}";
            var premiumUntil = coach.SubscriptionExpiresAtUtc?.ToString("yyyy-MM-dd");

            var text =
        $@"Hi {first},

Welcome aboard. Let’s set you up for success.

🎁 Premium Early Access is active until: {premiumUntil}

Get started:
• Finish your coach profile (specialties, certifications, and a clear bio)
• Add pricing and the services you offer
• Set availability and preferred training locations
• Enable notifications so you never miss a request

Go to your dashboard:
https://ptfindernow.com/dashboard

We’re here to help — reply to this email if you need assistance.

{EmailText.Footer}";

            await _sender.SendAsync(
                to: coach.Email,
                subject: subject,
                htmlBody: null,
                textBody: text,
                headers: FlowHeaders("welcome-coach"),
                tags: new[] { ("role", "coach"), ("flow", "welcome-coach") },
                fromOverride: SafeFrom(_smtp?.FromAddresses?.Welcome)
            );

            return CreatedAtAction(nameof(GetCoach), new { id = coach.Id }, coach);
        }


        // ─────────────────────────────────────────────────────────────────────────
        // PUT: api/coaches/{id}  (multipart/form-data)
        // ─────────────────────────────────────────────────────────────────────────
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCoach(int id, [FromForm] CoachUpdateDto dto)
        {
            var coach = await _context.Coaches.FindAsync(id);
            if (coach == null)
                return NotFound();

            // Only update fields that were actually sent

            if (!string.IsNullOrWhiteSpace(dto.FullName))
                coach.FullName = dto.FullName;

            if (!string.IsNullOrWhiteSpace(dto.Email))
                coach.Email = dto.Email;

            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                coach.PhoneNumber = dto.PhoneNumber;

            // Only change password if user sent something
            if (!string.IsNullOrWhiteSpace(dto.Password))
                coach.Password = dto.Password; // TODO: hash

            if (!string.IsNullOrWhiteSpace(dto.Gender))
                coach.Gender = dto.Gender;

            if (dto.Price.HasValue)
                coach.Price = dto.Price.Value;

            if (!string.IsNullOrWhiteSpace(dto.Description))
                coach.Description = dto.Description;

            if (dto.CategoryId.HasValue && dto.CategoryId.Value > 0)
                coach.CategoryId = dto.CategoryId.Value;

            if (dto.SpecialityId.HasValue && dto.SpecialityId.Value > 0)
                coach.SpecialityId = dto.SpecialityId.Value;

            if (dto.CountryId.HasValue && dto.CountryId.Value > 0)
                coach.CountryId = dto.CountryId.Value;

            if (dto.CityId.HasValue && dto.CityId.Value > 0)
                coach.CityId = dto.CityId.Value;

            if (dto.AreaId.HasValue && dto.AreaId.Value > 0)
                coach.AreaId = dto.AreaId.Value;

            // Profile image (optional)
            if (dto.ProfileImage != null && dto.ProfileImage.Length > 0)
            {
                var newName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);

                await using var stream = dto.ProfileImage.OpenReadStream();
                await _blobs.UploadAsync(newName, stream, dto.ProfileImage.ContentType);

                // delete old blob if any
                if (!string.IsNullOrWhiteSpace(coach.ProfileImage))
                    await _blobs.DeleteAsync(coach.ProfileImage);

                coach.ProfileImage = newName;
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DELETE: api/coaches/{id}
        // ─────────────────────────────────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCoach(int id)
        {
            var coach = await _context.Coaches.FindAsync(id);
            if (coach == null)
                return NotFound();

            // delete avatar blob if any
            if (!string.IsNullOrWhiteSpace(coach.ProfileImage))
            {
                await _blobs.DeleteAsync(coach.ProfileImage);
            }

            _context.Coaches.Remove(coach);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}

