﻿using Microsoft.AspNetCore.Http;
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

        // GET: api/Availabilities
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

        // GET: api/Availabilities/5
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

        // GET: api/Availabilities/find
        [HttpGet("find")]
        public async Task<IActionResult> FindAvailability(
         int coachId,
         DateTime? date = null,  // ✅ make nullable
         string timeSlot = null  // ✅ make nullable
     )
        {
            // Start with the base query
            var query = _context.Availabilities
                .Where(a => a.CoachId == coachId);

            // Apply date filter if provided
            if (date.HasValue)
            {
                query = query.Where(a => a.AvailableDate.Date == date.Value.Date);
            }

            // Apply timeSlot filter if provided
            if (!string.IsNullOrEmpty(timeSlot))
            {
                query = query.Where(a => a.TimeSlot == timeSlot);
            }

            // If timeSlot & date provided → return single (old behavior)
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

            // Otherwise → return full list
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



        // POST: api/Availabilities
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

        // PUT: api/Availabilities/5
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

        // DELETE: api/Availabilities/5
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
