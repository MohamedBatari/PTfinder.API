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

        public CoachesController(AppDbContext context, BlobStorageService blobs, IEmailSender sender, IOptions<SmtpSettings> smtp)
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

        // GET: api/coaches
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

        // GET: api/coaches/{id}
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

        // GET: api/coaches/check-email?email=...
        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmailExists(string email)
        {
            var coachExists = await _context.Coaches.AnyAsync(c => c.Email == email);
            return Ok(new { exists = coachExists });
        }

        // POST: api/coaches (multipart/form-data)
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
                EmailVerified = true // by now we have emailProof; keep true for convenience
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

            return CreatedAtAction("GetCoach", new { id = coach.Id }, coach);
        }

        // PUT & DELETE unchanged…
    }
}
