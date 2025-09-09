using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Helpers;
using PTfinder.API.Services;
using PTfinder.API.Settings;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using YourAppNamespace.Models.DTOs;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoachesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BlobStorageService _blobs;
        private readonly IConfiguration _cfg;
        private readonly IEmailSender _sender;
        private readonly SmtpSettings _smtp;

        public CoachesController(
            AppDbContext context,
            BlobStorageService blobs,
            IConfiguration cfg,
            IEmailSender sender,
            IOptions<SmtpSettings> smtp)
        {
            _context = context;
            _blobs = blobs;
            _cfg = cfg;
            _sender = sender;
            _smtp = smtp.Value;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Email helpers
        // ─────────────────────────────────────────────────────────────────────
        private string WebBaseUrl => _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";

        private Dictionary<string, string> FlowHeaders(string flow) => new()
        {
            { "List-Unsubscribe", "<mailto:unsubscribe@ptfindernow.com>, <https://ptfindernow.com/unsubscribe>" },
            { "List-Unsubscribe-Post", "List-Unsubscribe=One-Click" },
            { "Auto-Submitted", "auto-generated" },
            { "X-Auto-Response-Suppress", "All" },
            { "Feedback-ID", $"ptn-tx:{flow}:ptfindernow" }
        };

        private static IEnumerable<(string Name, string Value)> Tags(params (string, string)[] xs)
        {
            var list = new List<(string, string)>(xs)
            {
                ("type","transactional"),
                ("channel","email"),
                ("lang","en")
            };
            return list;
        }

        private string? SafeFrom(string? preferredFrom)
        {
            var chosen = string.IsNullOrWhiteSpace(preferredFrom)
                ? _smtp?.FromAddresses?.Default
                : preferredFrom;

            return string.IsNullOrWhiteSpace(chosen) ? null : chosen;
        }

        // ─────────────────────────────────────────────────────────────────────
        // JWT "email-proof" validator
        // ─────────────────────────────────────────────────────────────────────
        private string? ValidateEmailProof(HttpRequest req)
        {
            var auth = req.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;

            var token = auth.Substring("Bearer ".Length).Trim();
            var key = _cfg["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(key)) return null;

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var parameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
                var principal = handler.ValidateToken(token, parameters, out _);
                var type = principal.FindFirst("type")?.Value;
                var scope = principal.FindFirst("scope")?.Value;
                var email = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                return (type == "email-proof" && scope == "email-verified")
                    ? email?.Trim().ToLowerInvariant()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET: api/coaches
        // ─────────────────────────────────────────────────────────────────────
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
                Category = coach.Category?.Name,
                Speciality = new { coach.Speciality?.Id, coach.Speciality?.Name },
                Country = coach.Country?.Name,
                City = coach.City?.Name,
                Area = coach.Area?.Name,

                ProfileImage = string.IsNullOrWhiteSpace(coach.ProfileImage)
                    ? null
                    : _blobs.GetReadUrl(coach.ProfileImage, TimeSpan.FromMinutes(60)),

                Availabilities = coach.Availabilities.Select(a => new
                {
                    a.Id,
                    AvailableDate = a.AvailableDate.ToString("yyyy-MM-dd"),
                    a.TimeSlot
                }).ToList()
            });

            return Ok(response);
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET: api/coaches/{id}
        // ─────────────────────────────────────────────────────────────────────
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
                .FirstOrDefaultAsync(c => c.Id == id);

            if (coach == null)
                return NotFound();

            var response = new
            {
                coach.Id,
                coach.FullName,
                coach.Email,
                coach.PhoneNumber,
                coach.Gender,
                coach.Price,
                coach.Description,
                Category = coach.Category?.Name,
                Speciality = new { coach.Speciality?.Id, coach.Speciality?.Name },
                Country = coach.Country?.Name,
                City = coach.City?.Name,
                Area = coach.Area?.Name,

                ProfileImage = string.IsNullOrWhiteSpace(coach.ProfileImage)
                    ? null
                    : _blobs.GetReadUrl(coach.ProfileImage, TimeSpan.FromMinutes(60)),

                Availabilities = coach.Availabilities.Select(a => new
                {
                    a.Id,
                    AvailableDate = a.AvailableDate.ToString("yyyy-MM-dd"),
                    a.TimeSlot
                }).ToList()
            };

            return Ok(response);
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET: api/coaches/search
        // ─────────────────────────────────────────────────────────────────────
        [HttpGet("Search")]
        public async Task<IActionResult> Search([FromQuery] CoachSearchParams searchParams)
        {
            var query = _context.Coaches
                .Include(c => c.Category)
                .Include(c => c.Speciality)
                .Include(c => c.Country)
                .Include(c => c.City)
                .Include(c => c.Area)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchParams.CategoryName))
                query = query.Where(c => c.Category != null &&
                                         c.Category.Name.ToLower().Contains(searchParams.CategoryName.ToLower()));

            if (!string.IsNullOrWhiteSpace(searchParams.SpecialtyName))
                query = query.Where(c => c.Speciality != null &&
                                         c.Speciality.Name.ToLower().Contains(searchParams.SpecialtyName.ToLower()));

            if (!string.IsNullOrWhiteSpace(searchParams.CountryName))
                query = query.Where(c => c.Country != null &&
                                         c.Country.Name.ToLower().Contains(searchParams.CountryName.ToLower()));

            if (!string.IsNullOrWhiteSpace(searchParams.CityName))
                query = query.Where(c => c.City != null &&
                                         c.City.Name.ToLower().Contains(searchParams.CityName.ToLower()));

            if (!string.IsNullOrWhiteSpace(searchParams.AreaName))
                query = query.Where(c => c.Area != null &&
                                         c.Area.Name.ToLower().Contains(searchParams.AreaName.ToLower()));

            if (!string.IsNullOrWhiteSpace(searchParams.Gender))
                query = query.Where(c => !string.IsNullOrEmpty(c.Gender) &&
                                         c.Gender.ToLower().Contains(searchParams.Gender.ToLower()));

            var result = await query.Select(c => new
            {
                c.Id,
                c.FullName,
                ProfileImage = string.IsNullOrWhiteSpace(c.ProfileImage)
                    ? null
                    : _blobs.GetReadUrl(c.ProfileImage, TimeSpan.FromMinutes(60)),
                c.Price,
                c.Description,
                CategoryName = c.Category != null ? c.Category.Name : null,
                SpecialtyName = c.Speciality != null ? c.Speciality.Name : null,
                CountryName = c.Country != null ? c.Country.Name : null,
                CityName = c.City != null ? c.City.Name : null,
                AreaName = c.Area != null ? c.Area.Name : null
            }).ToListAsync();

            return Ok(result);
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET: api/coaches/check-email?email=...
        // ─────────────────────────────────────────────────────────────────────
        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmailExists([FromQuery] string email)
        {
            var e = (email ?? "").Trim().ToLowerInvariant();
            var coachExists = await _context.Coaches.AnyAsync(c => c.Email.ToLower() == e);
            return Ok(new { exists = coachExists });
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST: api/coaches   (multipart/form-data)
        // Requires valid "email-proof" token matching dto.Email
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<ActionResult> PostCoach([FromForm] CoachCreateDto dto, CancellationToken ct)
        {
            // 1) Validate email-proof
            var proofEmail = ValidateEmailProof(Request);
            if (proofEmail is null)
                return Unauthorized(new { error = "Email verification required." });

            var email = (dto.Email ?? "").Trim().ToLowerInvariant();
            if (email != proofEmail)
                return BadRequest(new { error = "Email mismatch." });

            // 2) Uniqueness
            var exists = await _context.Coaches.AnyAsync(c => c.Email.ToLower() == email, ct);
            if (exists) return Conflict(new { error = "Email already registered." });

            // 3) Upload image (store blob name)
            string? blobName = null;
            if (dto.ProfileImage != null && dto.ProfileImage.Length > 0)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);
                await using var stream = dto.ProfileImage.OpenReadStream();
                await _blobs.UploadAsync(fileName, stream, dto.ProfileImage.ContentType);
                blobName = fileName;
            }

            // 4) Create coach row (mark verified)
            var coach = new Coach
            {
                FullName = dto.FullName,
                Email = email,
                PhoneNumber = dto.PhoneNumber,
                // TODO: replace with a real hash (e.g., BCrypt). Keeping as-is to match your current model:
                Password = dto.Password,
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
                EmailVerificationToken = null,
                EmailVerificationExpiresUtc = null,
            };

            _context.Coaches.Add(coach);
            await _context.SaveChangesAsync(ct);

            // 5) Welcome email (after create)
            var first = (coach.FullName ?? "there").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
            var subject = $"Welcome to PTfinderNow, {first}";
            var dashboardUrl = $"{WebBaseUrl}/dashboard";
            var text =
$@"Hi {first},

Welcome aboard. Let’s set you up for success.

Get started:
• Finish your coach profile (specialties, certifications, and a clear bio)
• Add pricing and the services you offer
• Set availability and preferred training locations
• Enable notifications so you never miss a request

Go to your dashboard:
{dashboardUrl}

We’re here to help — reply to this email if you need assistance.

{EmailText.Footer}";

            await _sender.SendAsync(
                to: email,
                subject: subject,
                htmlBody: null,
                textBody: text,
                ct: ct,
                headers: FlowHeaders("welcome-after-register"),
                tags: Tags(("role", "coach"), ("flow", "welcome-after-register")),
                fromOverride: SafeFrom(_smtp?.FromAddresses?.Welcome)
            );

            // Note: your React expects success; 201 is fine for axios.
            return CreatedAtAction(nameof(GetCoach), new { id = coach.Id }, new { ok = true, id = coach.Id });
        }

        // ─────────────────────────────────────────────────────────────────────
        // PUT: api/coaches/{id}  (multipart/form-data)
        // ─────────────────────────────────────────────────────────────────────
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCoach(int id, [FromForm] CoachUpdateDto dto, CancellationToken ct)
        {
            var coach = await _context.Coaches.FindAsync(new object?[] { id }, ct);
            if (coach == null) return NotFound();

            coach.FullName = dto.FullName;
            coach.Email = (dto.Email ?? "").Trim().ToLowerInvariant();
            coach.PhoneNumber = dto.PhoneNumber;
            // TODO: hash if you add hashing model-wide
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

                if (!string.IsNullOrWhiteSpace(coach.ProfileImage))
                    await _blobs.DeleteAsync(coach.ProfileImage);

                coach.ProfileImage = newName;
            }

            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        // ─────────────────────────────────────────────────────────────────────
        // DELETE: api/coaches/{id}
        // ─────────────────────────────────────────────────────────────────────
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCoach(int id, CancellationToken ct)
        {
            var coach = await _context.Coaches.FindAsync(new object?[] { id }, ct);
            if (coach == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(coach.ProfileImage))
                await _blobs.DeleteAsync(coach.ProfileImage);

            _context.Coaches.Remove(coach);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}


