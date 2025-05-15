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
    public class AvailabilityController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AvailabilityController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAvailabilities()
        {
            var availabilities = await _context.Availabilities
                .Include(a => a.Coach)
                .Select(a => new
                {
                    a.Id,
                    a.CoachId,
                    CoachName = a.Coach.FullName,
                    a.AvailableDate,
                    a.TimeSlot
                })
                .ToListAsync();

            return Ok(availabilities);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetAvailabilityById(int id)
        {
            var availability = await _context.Availabilities
                .Include(a => a.Coach)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (availability == null)
                return NotFound();

            return Ok(new
            {
                availability.Id,
                availability.CoachId,
                CoachName = availability.Coach.FullName,
                availability.AvailableDate,
                availability.TimeSlot
            });
        }

        [HttpGet("find")]
        public async Task<IActionResult> FindAvailability(
         int coachId,
         DateTime? date = null, 
         string timeSlot = null  
     )
        {
            var query = _context.Availabilities
                .Where(a => a.CoachId == coachId);

            if (date.HasValue)
            {
                query = query.Where(a => a.AvailableDate.Date == date.Value.Date);
            }

            if (!string.IsNullOrEmpty(timeSlot))
            {
                query = query.Where(a => a.TimeSlot == timeSlot);
            }

            if (date.HasValue && !string.IsNullOrEmpty(timeSlot))
            {
                var availability = await query.FirstOrDefaultAsync();

                if (availability == null)
                    return NotFound();

                return Ok(new
                {
                    availability.Id,
                    availability.CoachId,
                    availability.AvailableDate,
                    availability.TimeSlot
                });
            }

            var availabilities = await query
                .Select(a => new
                {
                    a.Id,
                    a.CoachId,
                    a.AvailableDate,
                    a.TimeSlot
                })
                .ToListAsync();

            if (!availabilities.Any())
                return NotFound();

            return Ok(availabilities);
        }



        [HttpPost]
        public async Task<ActionResult<Availability>> CreateAvailability(AvailabilityCreateDto dto)
        {
            var availability = new Availability
            {
                CoachId = dto.CoachId,
                AvailableDate = dto.AvailableDate,
                TimeSlot = dto.TimeSlot
            };

            _context.Availabilities.Add(availability);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAvailabilityById), new { id = availability.Id }, availability);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAvailability(int id, AvailabilityUpdateDto dto)
        {
            var availability = await _context.Availabilities.FindAsync(id);
            if (availability == null)
                return NotFound();

            availability.AvailableDate = dto.AvailableDate;
            availability.TimeSlot = dto.TimeSlot;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAvailability(int id)
        {
            var availability = await _context.Availabilities.FindAsync(id);
            if (availability == null)
                return NotFound();

            _context.Availabilities.Remove(availability);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
