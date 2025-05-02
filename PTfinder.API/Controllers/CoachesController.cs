using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using Microsoft.EntityFrameworkCore;
using System.IO;
using YourAppNamespace.Models.DTOs;


namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CoachesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CoachesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Coaches
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
                Speciality = new
                {
                    coach.Speciality?.Id,
                    coach.Speciality?.Name
                },
                Country = coach.Country?.Name,
                City = coach.City?.Name,
                Area = coach.Area?.Name,
                coach.ProfileImage
            });

            return Ok(response);
        }

        // GET: api/Coaches/5
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
                Speciality = new
                {
                    coach.Speciality?.Id,
                    coach.Speciality?.Name
                },
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

            var result = await query.Select(c => new CoachSearchParams
            {
                Id = c.Id,
                FullName = c.FullName,
                ProfileImage = c.ProfileImage,
                Price = c.Price,
                Description = c.Description,
                CategoryName = c.Category != null ? c.Category.Name : null,
                SpecialtyName = c.Speciality != null ? c.Speciality.Name : null,
                CountryName = c.Country != null ? c.Country.Name : null,
                CityName = c.City != null ? c.City.Name : null,
                AreaName = c.Area != null ? c.Area.Name : null
            }).ToListAsync();

            return Ok(result);
        }



        // POST: api/Coaches
        [HttpPost]
        public async Task<ActionResult<Coach>> PostCoach([FromForm] CoachCreateDto dto)
        {
            string imageUrl = null;

            if (dto.ProfileImage != null)
            {
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(dto.ProfileImage.FileName);
                var filePath = Path.Combine("wwwroot/images", fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.ProfileImage.CopyToAsync(stream);
                }
                imageUrl = $"/images/{fileName}";
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

        // PUT: api/Coaches/5
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
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(dto.ProfileImage.FileName);
                var filePath = Path.Combine("wwwroot/images", fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.ProfileImage.CopyToAsync(stream);
                }

                coach.ProfileImage = $"/images/{fileName}";
            }

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/Coaches/5
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
    }
}


