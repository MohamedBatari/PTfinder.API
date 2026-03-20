using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Helpers;
using PTfinder.API.Services;
using PTfinder.API.Services.Emails; // ✅ correct

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoachesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BlobStorageService _blobs;
        private readonly IEmailSender _sender;
        private readonly ILogger<CoachesController> _logger;

        public CoachesController(
            AppDbContext context,
            BlobStorageService blobs,
            IEmailSender sender,
            ILogger<CoachesController> logger)
        {
            _context = context;
            _blobs = blobs;
            _sender = sender;
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

                // Flags
                coach.EmailVerified,
                coach.IsActive,
                coach.CreatedAtUtc,
                coach.UpdatedAtUtc,
                coach.CancelAtPeriodEnd,
                coach.CanceledAtUtc
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
                    coach.UpdatedAtUtc,
                    coach.CancelAtPeriodEnd,
                    coach.CanceledAtUtc
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetCoach({Id})", id);
                return StatusCode(500, new { message = "Error in GetCoach", error = ex.Message });
            }
        }

        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmailExists(string email)
        {
            var coachExists = await _context.Coaches.AnyAsync(c => c.Email == email);
            return Ok(new { exists = coachExists });
        }

        [HttpGet("Search")]
        public async Task<IActionResult> Search(
     [FromQuery] int? CategoryId,
     [FromQuery] int? SpecialityId,
     [FromQuery] int? CountryId,
     [FromQuery] int? CityId,
     [FromQuery] int? AreaId,
     [FromQuery] string? Gender,
     [FromQuery] string? FullName,
     [FromQuery] string? Sort = "Newest" // ✅ new
 )
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
                query = query.Where(c => !string.IsNullOrEmpty(c.Gender) && c.Gender.ToLower() == g);
            }

            if (!string.IsNullOrWhiteSpace(FullName))
            {
                var f = FullName.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().Contains(f));
            }

            // 🔥 ONLY active subscriptions
            query = query.Where(c =>
                c.IsActive &&
                c.EmailVerified &&
                c.SubscriptionTier > 0 &&
                (
                    (c.SubscriptionExpiresAtUtc.HasValue && c.SubscriptionExpiresAtUtc > nowUtc) ||
                    (c.CurrentPeriodEndUtc.HasValue && c.CurrentPeriodEndUtc > nowUtc)
                )
            );

            // ✅ Compute reviews/ratings (adjust table name/filters to your schema)
            var projected = query.Select(c => new
            {
                c.Id,
                c.FullName,
                c.CreatedAtUtc, // ✅ IMPORTANT (must exist in your Coach entity)

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

                // ✅ Reviews fields (example)
                NumReviews = _context.Reviews.Count(r => r.CoachId == c.Id),
                AvgRating = _context.Reviews
                    .Where(r => r.CoachId == c.Id)
                    .Select(r => (double?)r.Rating)
                    .Average() ?? 0.0,

                // subs
                c.SubscriptionTier,
                c.SubscriptionStatus,
                c.SubscriptionExpiresAtUtc,
                c.CurrentPeriodEndUtc
            });

            // ✅ Server-side sort (so it always works)
            Sort = (Sort ?? "Newest").Trim();

            projected = Sort switch
            {

                "ReviewsDesc" => projected.OrderByDescending(x => x.NumReviews),
                "Rating" => projected.OrderByDescending(x => x.AvgRating),
                "Popularity" => projected, // only if you have bookings count
                _ => projected.OrderByDescending(x => x.CreatedAtUtc) // ✅ Newest default
            };

            var result = await projected.ToListAsync();
            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<Coach>> PostCoach([FromForm] CoachCreateDto dto)
        {
            // ✅ Enforce Terms + Privacy acceptance
            if (!dto.TermsAccepted || string.IsNullOrWhiteSpace(dto.TermsVersion))
                return BadRequest(new { error = "You must accept the Terms and Conditions." });

            if (!dto.PrivacyAccepted || string.IsNullOrWhiteSpace(dto.PrivacyVersion))
                return BadRequest(new { error = "You must accept the Privacy Policy." });

            // ✅ sanity checks
            if (dto.TermsVersion.Length > 20)
                return BadRequest(new { error = "Invalid Terms version." });

            if (dto.PrivacyVersion.Length > 20)
                return BadRequest(new { error = "Invalid Privacy version." });

            // ✅ Confirm password (server-side)
            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 8)
                return BadRequest(new { error = "Password must be at least 8 characters." });

           
            // ✅ Capture IP (proxy header first)
            string? ip = null;
            var xff = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xff))
                ip = xff.Split(',').FirstOrDefault()?.Trim();
            ip ??= HttpContext?.Connection?.RemoteIpAddress?.ToString();

            // ✅ Capture User-Agent from request header (strong evidence)
            var uaHeader = Request.Headers["User-Agent"].ToString();
            var ua = !string.IsNullOrWhiteSpace(uaHeader)
                ? uaHeader
                : (string.IsNullOrWhiteSpace(dto.UserAgent) ? null : dto.UserAgent.Trim());

            // ✅ Normalize consent language
            var consentLang = string.IsNullOrWhiteSpace(dto.ConsentLanguage)
                ? null
                : dto.ConsentLanguage.Trim().ToLower();

            if (consentLang is not null && consentLang != "en" && consentLang != "ar")
                consentLang = null;

            string? blobName = null;

            if (dto.ProfileImage != null && dto.ProfileImage.Length > 0)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);
                await using var stream = dto.ProfileImage.OpenReadStream();
                await _blobs.UploadAsync(fileName, stream, dto.ProfileImage.ContentType);
                blobName = fileName;
            }

            var now = DateTime.UtcNow;

            // ✅ FREE PERIOD = 6 MONTHS
            var end = now.AddMonths(6);

            var categoryName = await _context.Categories
                .Where(x => x.Id == dto.CategoryId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync();

            var specialityName = await _context.Specialities
                .Where(x => x.Id == dto.SpecialityId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync();

            categoryName = string.IsNullOrWhiteSpace(categoryName) ? "your selected category" : categoryName;
            specialityName = string.IsNullOrWhiteSpace(specialityName) ? "your selected speciality" : specialityName;

            // ✅ IMPORTANT: hash password (do NOT store plain text)
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var coach = new Coach
            {
                FullName = dto.FullName?.Trim(),
                Email = dto.Email?.Trim().ToLower(),
                PhoneNumber = dto.PhoneNumber,

                // ✅ STORE HASH
                Password = hashedPassword,

                Gender = dto.Gender,
                Price = dto.Price,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                SpecialityId = dto.SpecialityId,
                CountryId = dto.CountryId,
                CityId = dto.CityId,
                AreaId = dto.AreaId,
                ProfileImage = blobName,

                EmailVerified = true,
                IsActive = true,

                // ✅ Free 6 months of Standard (AED 149 plan in your enum)
                SubscriptionTier = SubscriptionTier.Standard,
                SubscriptionStatus = SubscriptionStatus.Active,
                SubscriptionStartedAtUtc = now,
                SubscriptionExpiresAtUtc = end,
                CurrentPeriodEndUtc = end,

                StripeCustomerId = null,
                StripeSubscriptionId = null,

                // ✅ Evidence: server time + IP + UA
                TermsVersionAccepted = dto.TermsVersion.Trim(),
                TermsAcceptedAtUtc = now,
                TermsAcceptedIp = ip,

                PrivacyVersionAccepted = dto.PrivacyVersion.Trim(),
                PrivacyAcceptedAtUtc = now,
                PrivacyAcceptedIp = ip,
                PrivacyLanguage = consentLang,

                UserAgent = ua,
                ClientTimeZone = dto.ClientTimeZone,

                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _context.Coaches.Add(coach);
            await _context.SaveChangesAsync();

            // ✅ Welcome email
            var first = (coach.FullName ?? "there")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "there";

            var subject = "Welcome to PTfinderNow — Your Expert Profile Is Live ";
            var premiumUntil = end.ToString("yyyy-MM-dd");
            var logoUrl = "https://ptfindernow.com/images/PtFinderNow.png";

            var html = EmailTemplates.WelcomeCoachHtml(
                firstName: first,
                premiumUntil: premiumUntil,
                categoryName: categoryName,
                specialityName: specialityName,
                logoUrl: logoUrl,
                dashboardUrl: "https://ptfindernow.com/dashboard",
                supportEmail: "info@ptfindernow.com"
            );

            var text =
        $@"Hi {first},

Welcome to PTfinderNow 👋  
We’re excited to have you onboard.

Your expert profile is now live on PTfinderNow.

🚀 Your Early Access Benefits
As part of our early access program, your account includes premium features at no cost until: {premiumUntil}

✅ Consent record (for your reference)
Terms version: {coach.TermsVersionAccepted}
Privacy version: {coach.PrivacyVersionAccepted}
Accepted at: {now:yyyy-MM-dd HH:mm} UTC

👉 Go to your dashboard:
https://ptfindernow.com/dashboard

If you need help:
info@ptfindernow.com

Best regards,  
**The PTfinderNow Team**
{PTfinder.API.Helpers.EmailText.Footer}";

            await _sender.SendAsync(
                to: coach.Email,
                subject: subject,
                htmlBody: html,
                textBody: text,
                headers: FlowHeaders("welcome-coach-premium-prelaunch"),
                tags: new[] { ("role", "coach"), ("flow", "welcome-coach-premium-prelaunch") },
                fromOverride: null
            );

            return CreatedAtAction(nameof(GetCoach), new { id = coach.Id }, coach);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCoach(int id, [FromForm] CoachUpdateDto dto)
        {
            var coach = await _context.Coaches.FindAsync(id);
            if (coach == null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(dto.FullName))
                coach.FullName = dto.FullName;

            if (!string.IsNullOrWhiteSpace(dto.Email))
                coach.Email = dto.Email;

            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                coach.PhoneNumber = dto.PhoneNumber;

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

            if (dto.ProfileImage != null && dto.ProfileImage.Length > 0)
            {
                var newName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);

                await using var stream = dto.ProfileImage.OpenReadStream();
                await _blobs.UploadAsync(newName, stream, dto.ProfileImage.ContentType);

                if (!string.IsNullOrWhiteSpace(coach.ProfileImage))
                    await _blobs.DeleteAsync(coach.ProfileImage);

                coach.ProfileImage = newName;
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCoach(int id)
        {
            var coach = await _context.Coaches.FindAsync(id);
            if (coach == null)
                return NotFound();

            if (!string.IsNullOrWhiteSpace(coach.ProfileImage))
                await _blobs.DeleteAsync(coach.ProfileImage);

            _context.Coaches.Remove(coach);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
