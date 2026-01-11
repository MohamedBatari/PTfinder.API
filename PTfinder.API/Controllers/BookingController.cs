using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Enums;
using PTfinder.API.Services;
using PTfinder.API.Services.Emails;

namespace PTfinder.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notifications;
        private readonly IConfiguration _cfg;
        private readonly IBackgroundJobClient _jobs;

        public BookingController(
            AppDbContext context,
            INotificationService notifications,
            IConfiguration cfg,
            IBackgroundJobClient jobs)
        {
            _context = context;
            _notifications = notifications;
            _cfg = cfg;
            _jobs = jobs;
        }

        private string WebBaseUrl => _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";
        private string LogoUrl => _cfg["Branding:LogoUrl"] ?? $"{WebBaseUrl.TrimEnd('/')}/images/PtFinderNow.png";

        // ✅ read coachId from JWT (supports both "coachId" and NameIdentifier)
        private int? GetCoachId()
        {
            var v =
                User.FindFirst("coachId")?.Value ??
                User.FindFirst("CoachId")?.Value ?? // ✅ support old token
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                User.FindFirst("sub")?.Value;

            return int.TryParse(v, out var id) ? id : null;
        }

        // ✅ If BookingDate is saved as Dubai local (Unspecified), convert correctly to UTC for Hangfire
        private static DateTime DubaiLocalToUtc(DateTime bookingDate)
        {
            if (bookingDate.Kind == DateTimeKind.Utc) return bookingDate;

            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");

            if (bookingDate.Kind == DateTimeKind.Unspecified)
                return TimeZoneInfo.ConvertTimeToUtc(bookingDate, tz);

            return bookingDate.ToUniversalTime();
        }

        private static string DubaiWhenText(DateTime bookingDate) =>
            $"{bookingDate:yyyy-MM-dd HH:mm} (Asia/Dubai)";

        // ---------------------------
        // POST: Create booking (PUBLIC - clients can book without login)
        // ---------------------------
        [HttpPost]
        public async Task<ActionResult<Booking>> CreateBooking(BookingCreateDto dto, CancellationToken ct)
        {
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

            // ✅ Notification → Coach (DB + SignalR; usually fast)
            await _notifications.NotifyCoachBookingRequest(
                coachId: booking.CoachId,
                bookingId: booking.Id,
                clientName: booking.StudentName,
                serviceName: "session",
                startsAtLocal: booking.BookingDate,
                timezone: "Asia/Dubai",
                ct: ct
            );

            // ✅ Emails moved to background (fast API response)
            _jobs.Enqueue<IBookingEmailFlows>(x =>
                x.SendBookingCreatedEmails(booking.Id, CancellationToken.None)
            );

            return CreatedAtAction(nameof(GetBooking), new { id = booking.Id }, booking);
        }

        // ---------------------------
        // GET: booking by id (SECURE: only owner coach)
        // ---------------------------
        [Authorize]
        [HttpGet("{id}")]
        public async Task<ActionResult<Booking>> GetBooking(int id, CancellationToken ct)
        {
            var coachId = GetCoachId();
            if (coachId == null) return Unauthorized();

            var booking = await _context.Bookings
                .Include(b => b.Coach)
                .FirstOrDefaultAsync(b => b.Id == id && b.CoachId == coachId.Value, ct);

            if (booking == null)
                return NotFound();

            return Ok(booking);
        }

        // ---------------------------
        // GET: bookings by coach (SECURE)
        // IMPORTANT: do NOT trust coachId from URL.
        // Return only the logged-in coach bookings.
        // ---------------------------
        [Authorize]
        [HttpGet("coach/{coachId}")]
        public async Task<IActionResult> GetBookingsByCoachId(int coachId, CancellationToken ct)
        {
            var tokenCoachId = GetCoachId();
            if (tokenCoachId == null) return Unauthorized();

            // ✅ block reading other coach bookings
            if (coachId != tokenCoachId.Value) return Forbid();

            var bookings = await _context.Bookings
                .Where(b => b.CoachId == tokenCoachId.Value)
                .OrderByDescending(b => b.Id)
                .ToListAsync(ct);

            if (bookings.Count == 0)
                return NotFound("No bookings found for this coach.");

            return Ok(bookings);
        }

        // ---------------------------
        // PUT: update status (SECURE: only owner coach)
        // ---------------------------
        [Authorize]
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateBookingStatus(int id, [FromBody] BookingStatusDto statusDto, CancellationToken ct)
        {
            var coachId = GetCoachId();
            if (coachId == null) return Unauthorized();

            var booking = await _context.Bookings
                .Include(b => b.Coach)
                .FirstOrDefaultAsync(b => b.Id == id && b.CoachId == coachId.Value, ct);

            if (booking == null)
                return NotFound($"Booking with ID {id} not found.");

            booking.Status = statusDto.Status;
            await _context.SaveChangesAsync(ct);

            var startUtc = DubaiLocalToUtc(booking.BookingDate);

            if (statusDto.Status == BookingStatus.Accepted)
            {
                // ----------------
                // Reminders (24h / 2h before)
                // ----------------
                var run24 = startUtc.AddHours(-24);
                if (run24 > DateTime.UtcNow.AddMinutes(1))
                {
                    _jobs.Schedule<IBookingReminderEmails>(
                        x => x.SendStudentReminder(booking.Id, 24, CancellationToken.None),
                        run24
                    );
                }

                var run2 = startUtc.AddHours(-2);
                if (run2 > DateTime.UtcNow.AddMinutes(1))
                {
                    _jobs.Schedule<IBookingReminderEmails>(
                        x => x.SendStudentReminder(booking.Id, 2, CancellationToken.None),
                        run2
                    );
                }
                else
                {
                    // If inside the 2-hour window, still notify if session not started
                    if (startUtc > DateTime.UtcNow.AddMinutes(5))
                    {
                        _jobs.Enqueue<IBookingReminderEmails>(
                            x => x.SendStudentReminder(booking.Id, 2, CancellationToken.None)
                        );
                    }
                }

                // ----------------
                // ✅ Review request (3 hours AFTER session start)
                // ----------------
                var reviewAt = startUtc.AddHours(3);
                if (reviewAt > DateTime.UtcNow.AddMinutes(1))
                {
                    _jobs.Schedule<IBookingReminderEmails>(
                        x => x.SendStudentReviewRequest(booking.Id, CancellationToken.None),
                        reviewAt
                    );
                }
                else
                {
                    _jobs.Enqueue<IBookingReminderEmails>(
                        x => x.SendStudentReviewRequest(booking.Id, CancellationToken.None)
                    );
                }

                // ✅ Notification → Coach
                await _notifications.NotifyCoachBookingConfirmed(
                    coachId: booking.CoachId,
                    bookingId: booking.Id,
                    clientName: booking.StudentName,
                    serviceName: "session",
                    startsAtLocal: booking.BookingDate,
                    timezone: "Asia/Dubai",
                    ct: ct
                );

                // ✅ Student email in background
                _jobs.Enqueue<IBookingEmailFlows>(x =>
                    x.SendBookingAcceptedEmail(booking.Id, CancellationToken.None)
                );
            }
            else if (statusDto.Status == BookingStatus.Cancelled)
            {
                // ✅ Notification → Coach
                await _notifications.NotifyCoachBookingDeclined(
                    coachId: booking.CoachId,
                    bookingId: booking.Id,
                    clientName: booking.StudentName,
                    serviceName: "session",
                    startsAtLocal: booking.BookingDate,
                    timezone: "Asia/Dubai",
                    ct: ct
                );

                // ✅ Student email in background
                _jobs.Enqueue<IBookingEmailFlows>(x =>
                    x.SendBookingDeclinedEmail(booking.Id, CancellationToken.None)
                );
            }

            return NoContent();
        }

        // ---------------------------
        // DELETE: booking (SECURE: only owner coach)
        // ---------------------------
        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBooking(int id, CancellationToken ct)
        {
            var coachId = GetCoachId();
            if (coachId == null) return Unauthorized();

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == id && b.CoachId == coachId.Value, ct);

            if (booking == null)
                return NotFound();

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync(ct);

            return NoContent();
        }
    }
}
