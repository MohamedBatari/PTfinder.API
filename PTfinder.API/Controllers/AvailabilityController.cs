using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;

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
        public async Task<ActionResult<IEnumerable<object>>> GetAvailabilities(CancellationToken ct)
        {
            var availabilities = await _context.Availabilities
                .Include(a => a.Coach)
                .Select(a => new
                {
                    a.Id,
                    a.CoachId,
                    CoachName = a.Coach != null ? a.Coach.FullName : null,
                    a.AvailableDate,
                    a.TimeSlot
                })
                .ToListAsync(ct);

            return Ok(availabilities);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<object>> GetAvailabilityById(int id, CancellationToken ct)
        {
            var availability = await _context.Availabilities
                .Include(a => a.Coach)
                .FirstOrDefaultAsync(a => a.Id == id, ct);

            if (availability == null)
                return NotFound();

            return Ok(new
            {
                availability.Id,
                availability.CoachId,
                CoachName = availability.Coach != null ? availability.Coach.FullName : null,
                availability.AvailableDate,
                availability.TimeSlot
            });
        }

        // GET: api/availability/find?coachId=1&date=2025-03-01&timeSlot=08:00-09:00
        [HttpGet("find")]
        public async Task<IActionResult> FindAvailability(
            [FromQuery] int coachId,
            [FromQuery] DateTime? date = null,
            [FromQuery] string? timeSlot = null,
            CancellationToken ct = default)
        {
            var query = _context.Availabilities.AsQueryable()
                .Where(a => a.CoachId == coachId);

            if (date.HasValue)
            {
                var d = date.Value.Date;
                query = query.Where(a => a.AvailableDate.Date == d);
            }

            if (!string.IsNullOrWhiteSpace(timeSlot))
            {
                query = query.Where(a => a.TimeSlot == timeSlot);
            }

            if (date.HasValue && !string.IsNullOrWhiteSpace(timeSlot))
            {
                var availability = await query.FirstOrDefaultAsync(ct);
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
                .ToListAsync(ct);

            if (availabilities.Count == 0)
                return NotFound();

            return Ok(availabilities);
        }

        [HttpPost]
        public async Task<ActionResult<Availability>> CreateAvailability(
            [FromBody] AvailabilityCreateDto dto,
            CancellationToken ct)
        {
            var availability = new Availability
            {
                CoachId = dto.CoachId,
                AvailableDate = dto.AvailableDate,
                TimeSlot = dto.TimeSlot
            };

            _context.Availabilities.Add(availability);
            await _context.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetAvailabilityById), new { id = availability.Id }, availability);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAvailability(
            int id,
            [FromBody] AvailabilityUpdateDto dto,
            CancellationToken ct)
        {
            var availability = await _context.Availabilities.FindAsync(new object?[] { id }, ct);
            if (availability == null)
                return NotFound();

            availability.AvailableDate = dto.AvailableDate;
            availability.TimeSlot = dto.TimeSlot;

            await _context.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteAvailability(int id, CancellationToken ct)
        {
            var availability = await _context.Availabilities.FindAsync(new object?[] { id }, ct);
            if (availability == null)
                return NotFound();

            _context.Availabilities.Remove(availability);
            await _context.SaveChangesAsync(ct);
            return NoContent();
        }
    }
}
