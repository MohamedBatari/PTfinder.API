using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using Microsoft.EntityFrameworkCore;
using System.IO;
using YourAppNamespace.Models.DTOs;
using Supabase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoachesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Supabase.Client _supabase;

        public CoachesController(AppDbContext context)
        {
            _context = context;

            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");

            _supabase = new Supabase.Client(supabaseUrl, supabaseKey);
            _supabase.InitializeAsync().Wait();
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
                coach.ProfileImage
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
                coach.ProfileImage
            };

            return Ok(response);
        }

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

            if (!string.IsNullOrEmpty(searchParams.CategoryName))
                query = query.Where(c => c.Category.Name.ToLower() == searchParams.CategoryName.ToLower());

            if (!string.IsNullOrEmpty(searchParams.SpecialtyName))
                query = query.Where(c => c.Speciality.Name.ToLower() == searchParams.SpecialtyName.ToLower());

            if (!string.IsNullOrEmpty(searchParams.CountryName))
                query = query.Where(c => c.Country.Name.ToLower() == searchParams.CountryName.ToLower());

            if (!string.IsNullOrEmpty(searchParams.CityName))
                query = query.Where(c => c.City.Name.ToLower() == searchParams.CityName.ToLower());

            if (!string.IsNullOrEmpty(searchParams.AreaName))
                query = query.Where(c => c.Area.Name.ToLower() == searchParams.AreaName.ToLower());

            if (!string.IsNullOrEmpty(searchParams.Gender))
                query = query.Where(c => c.Gender.ToLower() == searchParams.Gender.ToLower());

            if (searchParams.Price != null)
                query = query.Where(c => c.Price == searchParams.Price);

            var result = await query.Select(c => new
            {
                c.Id,
                c.FullName,
                c.ProfileImage,
                c.Price,
                c.Description,
                CategoryName = c.Category.Name,
                SpecialtyName = c.Speciality.Name,
                CountryName = c.Country.Name,
                CityName = c.City.Name,
                AreaName = c.Area.Name
            }).ToListAsync();

            return Ok(result);
        }



        [HttpPost]
        public async Task<ActionResult<Coach>> PostCoach([FromForm] CoachCreateDto dto)
        {
            string imageUrl = null;

            if (dto.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);
                await using var stream = dto.ProfileImage.OpenReadStream();
                var fileBytes = ReadStreamToByteArray(stream);

                var uploaded = await _supabase.Storage
                    .From("coach-images")
                    .Upload(fileBytes, fileName, new Supabase.Storage.FileOptions
                    {
                        ContentType = dto.ProfileImage.ContentType,
                        Upsert = true
                    });

                if (dto.ProfileImage != null)  // Change this line
                    return StatusCode(500, "Failed to upload image to Supabase.");

                imageUrl = _supabase.Storage.From("coach-images").GetPublicUrl(fileName);
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
                ProfileImage = imageUrl
            };

            _context.Coaches.Add(coach);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetCoach", new { id = coach.Id }, coach);
        }

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

            if (dto.ProfileImage != null)
            {
                var fileName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);
                await using var stream = dto.ProfileImage.OpenReadStream();
                var fileBytes = ReadStreamToByteArray(stream);

                var uploaded = await _supabase.Storage
                    .From("coach-images")
                    .Upload(fileBytes, fileName, new Supabase.Storage.FileOptions
                    {
                        ContentType = dto.ProfileImage.ContentType,
                        Upsert = true
                    });

                if (dto.ProfileImage != null)  // Change this line
                    return StatusCode(500, "Failed to upload image to Supabase.");

                coach.ProfileImage = _supabase.Storage.From("coach-images").GetPublicUrl(fileName);
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

