using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA.DTO;
using PTfinder.API.Enums;
using PTfinder.API.Services;
using PTfinder.API.Services.Emails;
using Hangfire;
using System.Security.Claims;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;


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
        private readonly IBackgroundJobClient _jobs;

        public BookingController(
            AppDbContext context,
            INotificationService notifications,
            IEmailSender sender,
            IConfiguration cfg,
            IBackgroundJobClient jobs)
        {
            _context = context;
            _notifications = notifications;
            _sender = sender;
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
                User.FindFirst("CoachId")?.Value ??                      // ✅ support old token
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

            // Notification → Coach
            await _notifications.NotifyCoachBookingRequest(
                coachId: booking.CoachId,
                bookingId: booking.Id,
                clientName: booking.StudentName,
                serviceName: "session",
                startsAtLocal: booking.BookingDate,
                timezone: "Asia/Dubai",
                ct: ct
            );

            var whenText = DubaiWhenText(booking.BookingDate);
            var coachManageUrl = $"{WebBaseUrl}/dashboard/bookings/{booking.Id}";

            // Email → Coach
            var coachHtml = EmailTemplates.BookingRequestCoachHtml(
                coachName: coach.FullName ?? "Coach",
                studentName: booking.StudentName ?? "",
                studentEmail: booking.StudentEmail ?? "",
                studentPhone: booking.StudentPhone ?? "",
                whenText: whenText,
                timeSlot: booking.TimeSlot ?? "",
                manageUrl: coachManageUrl,
                logoUrl: "https://ptfindernow.com/images/PtFinderNow.png"
            );

            var coachText =
$@"You have a new booking request.

Client: {booking.StudentName}
Email: {booking.StudentEmail}
Phone: {booking.StudentPhone}
Date/Time: {whenText}
Time Slot: {booking.TimeSlot}

Open in dashboard:
{coachManageUrl}

— PTfinderNow";

            await _sender.SendAsync(
                to: coach.Email,
                subject: $"New booking request — session from {booking.StudentName}",
                htmlBody: coachHtml,
                textBody: coachText,
                ct: ct
            );

            // Email → Student
            var studentHtml = EmailTemplates.BookingRequestStudentHtml(
                studentName: booking.StudentName ?? "Client",
                coachName: coach.FullName ?? "Coach",
                whenText: whenText,
                timeSlot: booking.TimeSlot ?? "",
                logoUrl: "https://ptfindernow.com/images/PtFinderNow.png"
            );

            var studentText =
$@"Your booking request was sent to {coach.FullName}.

Requested time: {whenText}
Time Slot: {booking.TimeSlot}

You'll receive an email when the coach confirms or declines.

— PTfinderNow";

            await _sender.SendAsync(
                to: booking.StudentEmail,
                subject: $"Your request was sent to {coach.FullName} — PTfinderNow",
                htmlBody: studentHtml,
                textBody: studentText,
                ct: ct
            );

            // You can keep this, but note: it returns booking object publicly.
            // It's OK, but if you want, return only booking.Id instead.
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

            var coach = booking.Coach!;
            var whenText = DubaiWhenText(booking.BookingDate);

            if (statusDto.Status == BookingStatus.Accepted)
            {
                var startUtc = DubaiLocalToUtc(booking.BookingDate);

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
                    // ✅ If we're inside the 2-hour window (or it already passed), still notify
                    // as long as session hasn't started yet.
                    if (startUtc > DateTime.UtcNow.AddMinutes(5))
                    {
                        _jobs.Enqueue<IBookingReminderEmails>(
                            x => x.SendStudentReminder(booking.Id, 2, CancellationToken.None)
                        );
                    }
                }


                await _notifications.NotifyCoachBookingConfirmed(
                    coachId: booking.CoachId,
                    bookingId: booking.Id,
                    clientName: booking.StudentName,
                    serviceName: "session",
                    startsAtLocal: booking.BookingDate,
                    timezone: "Asia/Dubai",
                    ct: ct);

                var html = EmailTemplates.BookingAcceptedStudentHtml(
                    studentName: booking.StudentName ?? "Client",
                    coachName: coach.FullName ?? "Coach",
                    whenText: whenText,
                    timeSlot: booking.TimeSlot ?? "",
                    logoUrl: "https://ptfindernow.com/images/PtFinderNow.png"
                );

                var text =
$@"Booking confirmed ✅

Coach: {coach.FullName}
Date/Time: {whenText}
Time Slot: {booking.TimeSlot}

— PTfinderNow";

                await _sender.SendAsync(
                    to: booking.StudentEmail,
                    subject: $"Booking confirmed — with {coach.FullName} (PTfinderNow)",
                    htmlBody: html,
                    textBody: text,
                    ct: ct);
            }
            else if (statusDto.Status == BookingStatus.Cancelled)
            {
                await _notifications.NotifyCoachBookingDeclined(
                    coachId: booking.CoachId,
                    bookingId: booking.Id,
                    clientName: booking.StudentName,
                    serviceName: "session",
                    startsAtLocal: booking.BookingDate,
                    timezone: "Asia/Dubai",
                    ct: ct);

                var searchUrl = $"{WebBaseUrl}/coaches/search";

                var html = EmailTemplates.BookingDeclinedStudentHtml(
                    studentName: booking.StudentName ?? "Client",
                    coachName: coach.FullName ?? "Coach",
                    whenText: whenText,
                    timeSlot: booking.TimeSlot ?? "",
                    searchUrl: searchUrl,
                    logoUrl: "https://ptfindernow.com/images/PtFinderNow.png"
                );

                var text =
$@"Booking declined ❌

Coach: {coach.FullName}
Requested time: {whenText}
Time Slot: {booking.TimeSlot}

Search for another coach:
{searchUrl}

— PTfinderNow";

                await _sender.SendAsync(
                    to: booking.StudentEmail,
                    subject: $"{coach.FullName} declined your request — PTfinderNow",
                    htmlBody: html,
                    textBody: text,
                    ct: ct);
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

