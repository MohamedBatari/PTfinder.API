using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA.DTO;
using PTfinder.API.Enums;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BookingController(AppDbContext context)
        {
            _context = context;
        }

        // POST: api/bookings
        [HttpPost]
        public async Task<ActionResult<Booking>> CreateBooking(BookingCreateDto dto)
        {
            // Check if the coach exists
            var coach = await _context.Coaches.FindAsync(dto.CoachId);
            if (coach == null)
            {
                return NotFound($"Coach with ID {dto.CoachId} not found.");
            }

            var booking = new Booking
            {
                CoachId = dto.CoachId,
                StudentName = dto.StudentName,
                StudentEmail = dto.StudentEmail,
                StudentPhone = dto.StudentPhone,
                BookingDate = dto.BookingDate,
                TimeSlot = dto.TimeSlot,
                Status = BookingStatus.Pending // Default status is Pending
            };

            // Add the booking to the database
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            // Return the newly created booking
            return CreatedAtAction(nameof(GetBooking), new { id = booking.Id }, booking);
        }

        [HttpGet("coach/{coachId}")]
        public async Task<IActionResult> GetBookingsByCoachId(int coachId)
        {
            var bookings = await _context.Bookings
                .Where(b => b.CoachId == coachId)
                .ToListAsync();

            if (bookings == null || !bookings.Any())
            {
                return NotFound("No bookings found for this coach.");
            }

            return Ok(bookings);
        }


        // GET: api/bookings/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Booking>> GetBooking(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.Coach) // Optionally include related Coach data
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
            {
                return NotFound();
            }

            return Ok(booking);
        }

        // DELETE: api/bookings/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBooking(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
            {
                return NotFound();
            }

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}

