using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA.DTO;
using PTfinder.API.Enums;
using PTfinder.API.Services; 

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notifications;
        private readonly IEmailSender _sender;
        private readonly IConfiguration _cfg;

        public BookingController(
            AppDbContext context,
            INotificationService notifications,
            IEmailSender sender,
            IConfiguration cfg)
        {
            _context = context;
            _notifications = notifications;
            _sender = sender;
            _cfg = cfg;
        }

        private string WebBaseUrl => _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";

        [HttpPost]
        public async Task<ActionResult<Booking>> CreateBooking(BookingCreateDto dto, CancellationToken ct)
        {
            // Basic guard
            if (!ModelState.IsValid)
                return BadRequest(new { error = "Invalid request." });

            var coach = await _context.Coaches.FirstOrDefaultAsync(c => c.Id == dto.CoachId, ct);
            if (coach == null)
                return NotFound($"Coach with ID {dto.CoachId} not found.");

            var booking = new Booking
            {
                CoachId = dto.CoachId,
                StudentName = dto.StudentName,
                StudentEmail = dto.StudentEmail,
                StudentPhone = dto.StudentPhone,
                BookingDate = dto.BookingDate,
                TimeSlot = dto.TimeSlot,
                Status = BookingStatus.Pending
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync(ct);

            // ─────────────────────────────────────────────────────
            // Real-time notification → Coach
            // ─────────────────────────────────────────────────────
            // serviceName/timezone: customize as you like or extend DTO
            var serviceName = "session";
            var timezone = "Asia/Dubai";
            await _notifications.NotifyCoachBookingRequest(
                coachId: booking.CoachId,
                bookingId: booking.Id,
                clientName: booking.StudentName,
                serviceName: serviceName,
                startsAtLocal: booking.BookingDate,
                timezone: timezone,
                ct: ct
            );

            // ─────────────────────────────────────────────────────
            // Transactional emails (simple text)
            // ─────────────────────────────────────────────────────
            var when = $"{booking.BookingDate:yyyy-MM-dd HH:mm}";
            var manageUrl = $"{WebBaseUrl}/dashboard/bookings/bookings/{booking.Id}";

            // Email → Coach
            await _sender.SendAsync(
                to: coach.Email,
                subject: $"New booking request — {serviceName} from {booking.StudentName}",
                htmlBody: null,
                textBody:
$@"You have a new booking request.

Client: {booking.StudentName}
Email: {booking.StudentEmail}
Phone: {booking.StudentPhone}
Date/Time: {when} (local)
Time Slot: {booking.TimeSlot}

Manage this request:
{manageUrl}

— PTfinderNow"
            , ct: ct);

            // Email → Student (receipt)
            await _sender.SendAsync(
                to: booking.StudentEmail,
                subject: $"We sent your request to {coach.FullName} — ",
                htmlBody: null,
                textBody:
$@"Your booking request was sent to {coach.FullName}.

Requested time: {when}
Time Slot: {booking.TimeSlot}

We'll email you when the coach confirms or proposes a new time.


— PTfinderNow"
            , ct: ct);

            return CreatedAtAction(nameof(GetBooking), new { id = booking.Id }, booking);
        }

        [HttpGet("coach/{coachId}")]
        public async Task<IActionResult> GetBookingsByCoachId(int coachId, CancellationToken ct)
        {
            var bookings = await _context.Bookings
                .Where(b => b.CoachId == coachId)
                .OrderByDescending(b => b.Id)
                .ToListAsync(ct);

            if (bookings == null || !bookings.Any())
                return NotFound("No bookings found for this coach.");

            return Ok(bookings);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Booking>> GetBooking(int id, CancellationToken ct)
        {
            var booking = await _context.Bookings
                .Include(b => b.Coach)
                .FirstOrDefaultAsync(b => b.Id == id, ct);

            if (booking == null)
                return NotFound();

            return Ok(booking);
        }

        // Controllers/BookingController.cs  (full method)
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateBookingStatus(int id, [FromBody] BookingStatusDto statusDto, CancellationToken ct)
        {
            var booking = await _context.Bookings
                .Include(b => b.Coach)
                .FirstOrDefaultAsync(b => b.Id == id, ct);

            if (booking == null)
                return NotFound($"Booking with ID {id} not found.");

            var previous = booking.Status;
            booking.Status = statusDto.Status;
            await _context.SaveChangesAsync(ct);

            // Common bits
            var coach = booking.Coach!;
            var serviceName = "Personal training session";     // tweak if you store service name
            var timezone = "Asia/Dubai";                       // tweak if you store user tz
            var when = $"{booking.BookingDate:yyyy-MM-dd HH:mm}";
            var manageUrl = $"{WebBaseUrl}/dashboard/bookings/bookings/{booking.Id}";

            // Email → Student; Notification → Coach
            if (statusDto.Status == BookingStatus.Accepted)
            {
                // notify coach (receipt)
                await _notifications.NotifyCoachBookingConfirmed(
                    coachId: booking.CoachId,
                    bookingId: booking.Id,
                    clientName: booking.StudentName,
                    serviceName: serviceName,
                    startsAtLocal: booking.BookingDate,
                    timezone: timezone,
                    ct: ct);

                // email student
                await _sender.SendAsync(
                    to: booking.StudentEmail,
                    subject: $"Booking confirmed — with {coach.FullName}",
                    htmlBody: null,
                    textBody:
        $@"Your booking is confirmed.

Coach: {coach.FullName}
Date/Time: {when}
Time Slot: {booking.TimeSlot}

Manage your booking:

— PTfinderNow",
                    ct: ct);
            }
            else if (statusDto.Status == BookingStatus.Cancelled)
            {
                // notify coach (receipt)
                await _notifications.NotifyCoachBookingDeclined(
                    coachId: booking.CoachId,
                    bookingId: booking.Id,
                    clientName: booking.StudentName,
                    serviceName: serviceName,
                    startsAtLocal: booking.BookingDate,
                    timezone: timezone,
                    ct: ct);

                // email student
                await _sender.SendAsync(
                    to: booking.StudentEmail,
                    subject: $"{coach.FullName} declined your request — ",
                    htmlBody: null,
                    textBody:
        $@"Your booking request was declined by {coach.FullName}.

Requested time: {when}
Time Slot: {booking.TimeSlot}

You can try a different time or search for another coach:
{WebBaseUrl}/Coaches/search

— PTfinderNow",
                    ct: ct);
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBooking(int id, CancellationToken ct)
        {
            var booking = await _context.Bookings.FindAsync(new object?[] { id }, ct);
            if (booking == null)
                return NotFound();

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync(ct);

            return NoContent();
        }
    }
}