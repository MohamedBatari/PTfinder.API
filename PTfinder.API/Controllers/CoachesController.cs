using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public CoachesController(
            AppDbContext context,
            BlobStorageService blobs,
            IEmailSender sender,
            IOptions<SmtpSettings> smtp)
        {
            _context = context;
            _blobs = blobs;
            _sender = sender;
            _smtp = smtp.Value;
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

            if (coach == null) return NotFound();

            var response = new
            {
                coach.Id,
                coach.FullName,
                coach.Email,
                coach.PhoneNumber,
                coach.Gender,
                coach.Price,
                coach.Description,

                Country = coach.Country?.Name,
                City = coach.City?.Name,
                Area = coach.Area?.Name,

                Category = coach.Category?.Name,
                Speciality = new { coach.Speciality?.Id, coach.Speciality?.Name },

                ProfileImage = string.IsNullOrWhiteSpace(coach.ProfileImage)
                    ? null
                    : _blobs.GetReadUrl(coach.ProfileImage, TimeSpan.FromMinutes(60)),

                Availabilities = coach.Availabilities.Select(a => new
                {
                    a.Id,
                    AvailableDate = a.AvailableDate.ToString("yyyy-MM-dd"),
                    a.TimeSlot
                }),



                coach.SubscriptionTier,
                coach.SubscriptionStatus,
                coach.SubscriptionStartedAtUtc,
                coach.SubscriptionExpiresAtUtc,
                coach.CurrentPeriodEndUtc,
                coach.StripeCustomerId,
                coach.StripeSubscriptionId,

             

                coach.EmailVerified,
                coach.IsActive,
                coach.CreatedAtUtc,
                coach.UpdatedAtUtc
            };

            return Ok(response);
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
                    AreaName = c.Area != null ? c.Area.Name : null
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

            var coach = new Coach
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber,
                Password = dto.Password, // TODO: hash in production
                Gender = dto.Gender,
                Price = dto.Price,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                SpecialityId = dto.SpecialityId,
                CountryId = dto.CountryId,
                CityId = dto.CityId,
                AreaId = dto.AreaId,
                ProfileImage = blobName,
                EmailVerified = true // email has been verified via OTP at signup
            };

            _context.Coaches.Add(coach);
            await _context.SaveChangesAsync();

            // Send Welcome email (coach)
            var first = (coach.FullName ?? "there").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
            var subject = $"Welcome to PTfinderNow, {first}";
            var text =
$@"Hi {first},

Welcome aboard. Let’s set you up for success.

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

            coach.FullName = dto.FullName;
            coach.Email = dto.Email;
            coach.PhoneNumber = dto.PhoneNumber;
            coach.Password = dto.Password;
            coach.Gender = dto.Gender;
            coach.Price = dto.Price;
            coach.Description = dto.Description;
            coach.CategoryId = dto.CategoryId;
            coach.SpecialityId = dto.SpecialityId;
            coach.CountryId = dto.CountryId;
            coach.CityId = dto.CityId;
            coach.AreaId = dto.AreaId;

            if (dto.ProfileImage != null && dto.ProfileImage.Length > 0)
            {
                var newName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);

                await using var stream = dto.ProfileImage.OpenReadStream();
                await _blobs.UploadAsync(newName, stream, dto.ProfileImage.ContentType);

                // delete old blob if any
                if (!string.IsNullOrWhiteSpace(coach.ProfileImage))
                    await _blobs.DeleteAsync(coach.ProfileImage);

                coach.ProfileImage = newName; // store new blob name
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

