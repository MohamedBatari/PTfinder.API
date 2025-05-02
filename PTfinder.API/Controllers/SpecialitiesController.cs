using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using Microsoft.EntityFrameworkCore;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SpecialitiesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SpecialitiesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Speciality>> GetSpeciality(int id)
        {
            var speciality = await _context.Specialities
                .Include(s => s.Category) // if you want to include the related category
                .FirstOrDefaultAsync(s => s.Id == id);

            if (speciality == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                speciality.Id,
                speciality.Name,
                Category = speciality.Category == null ? null : new
                {
                    speciality.CategoryId,
                    speciality.Category.Name
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetSpecialities([FromQuery] int? categoryId)
        {
            var query = _context.Specialities.AsQueryable();

            if (categoryId.HasValue)
            {
                query = query.Where(s => s.CategoryId == categoryId.Value);
            }

            var result = await query.ToListAsync();
            return Ok(result);
        }



        [HttpPost]
        public async Task<IActionResult> Create(SpecialityCreateDto dto)
        {
            var speciality = new Speciality
            {
                Name = dto.Name,
                CategoryId = dto.CategoryId
            };

            _context.Specialities.Add(speciality);
            await _context.SaveChangesAsync();

            // Changed GetById to GetSpeciality
            return CreatedAtAction(nameof(GetSpeciality), new { id = speciality.Id }, speciality);
        }




        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, SpecialityUpdateDto dto)
        {
            var speciality = await _context.Specialities.FindAsync(id);
            if (speciality == null) return NotFound();

            speciality.Name = dto.Name;
            speciality.CategoryId = dto.CategoryId;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var speciality = await _context.Specialities.FindAsync(id);
            if (speciality == null) return NotFound();

            _context.Specialities.Remove(speciality);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
