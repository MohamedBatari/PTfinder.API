using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using Microsoft.EntityFrameworkCore;
using System.IO;
using YourAppNamespace.Models.DTOs; // keep if you actually use types from here; otherwise remove
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PTfinder.API.Services;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoachesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BlobStorageService _blobs;

        public CoachesController(AppDbContext context, BlobStorageService blobs)
        {
            _context = context;
            _blobs = blobs;
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

                // Store blob name in DB, return SAS URL to client
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

                // Return SAS URL
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

        // GET: api/coaches/search
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

                // Return SAS URL
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

        // GET: api/coaches/check-email?email=...
        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmailExists(string email)
        {
            var coachExists = await _context.Coaches.AnyAsync(c => c.Email == email);
            return Ok(new { exists = coachExists });
        }

        // POST: api/coaches   (multipart/form-data)
        [HttpPost]
        public async Task<ActionResult<Coach>> PostCoach([FromForm] CoachCreateDto dto)
        {
            string? blobName = null;

            if (dto.ProfileImage != null && dto.ProfileImage.Length > 0)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);

                await using var stream = dto.ProfileImage.OpenReadStream();
                await _blobs.UploadAsync(fileName, stream, dto.ProfileImage.ContentType);

                // store the blob name (NOT a public URL)
                blobName = fileName;
            }

            var coach = new Coach
            {
                FullName = dto.FullName,
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber,
                Password = dto.Password,
                Gender = dto.Gender,
                Price = dto.Price,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                SpecialityId = dto.SpecialityId,
                CountryId = dto.CountryId,
                CityId = dto.CityId,
                AreaId = dto.AreaId,

                // store blob name in DB
                ProfileImage = blobName
            };

            _context.Coaches.Add(coach);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetCoach", new { id = coach.Id }, coach);
        }

        // PUT: api/coaches/{id}  (multipart/form-data)
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

        // DELETE: api/coaches/{id}
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

        private static byte[] ReadStreamToByteArray(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
    }
}

