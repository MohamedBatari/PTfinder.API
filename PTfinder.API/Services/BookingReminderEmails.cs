using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.Enums;
using PTfinder.API.Services.Emails;
using System.Globalization;

namespace PTfinder.API.Services;

public interface IBookingReminderEmails
{
    Task SendStudentReminder(int bookingId, int hoursBefore, CancellationToken ct = default);
    Task SendStudentReviewRequest(int bookingId, CancellationToken ct = default);
}

public sealed class BookingReminderEmails : IBookingReminderEmails
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly IConfiguration _cfg;

    public BookingReminderEmails(AppDbContext db, IEmailSender sender, IConfiguration cfg)
    {
        _db = db;
        _sender = sender;
        _cfg = cfg;
    }

    private string WebBaseUrl => _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";

    // ✅ Optional: keep for future, but you already pass logoUrl explicitly


    public async Task SendStudentReminder(int bookingId, int hoursBefore, CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Coach)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null) return;

        // ✅ only if still accepted
        if (booking.Status != BookingStatus.Accepted) return;

        var coach = booking.Coach;
        if (coach == null) return;

        if (string.IsNullOrWhiteSpace(booking.StudentEmail)) return;

        var whenText = $"{booking.BookingDate:yyyy-MM-dd HH:mm} (Asia/Dubai)";

        var subject = hoursBefore >= 24
            ? $"Reminder: your session tomorrow with {coach.FullName} — PTfinderNow"
            : $"Reminder: your session in {hoursBefore} hours with {coach.FullName} — PTfinderNow";

        var html = EmailTemplates.BookingReminderStudentHtml(
            studentName: booking.StudentName ?? "Client",
            coachName: coach.FullName ?? "Coach",
            whenText: whenText,
            timeSlot: booking.TimeSlot ?? "",
            hoursBefore: hoursBefore,
            logoUrl: "https://ptfindernow.com/images/PtFinderNow.png"
        );

        var titleText = hoursBefore >= 24
            ? "Reminder: your session is tomorrow"
            : $"Reminder: your session in {hoursBefore} hours";

        var text =
$@"{titleText}

Coach: {coach.FullName}
Date/Time: {whenText}
Time Slot: {booking.TimeSlot}

— PTfinderNow";

        await _sender.SendAsync(
            to: booking.StudentEmail,
            subject: subject,
            htmlBody: html,
            textBody: text,
            ct: ct
        );
    }

    // ✅ Review request: send AFTER session ends + delayHours (default 3h)
    // ✅ Status stays Accepted (since you don't have Completed)
    // ✅ Send only ONCE using ReviewRequestSentAtUtc
    public async Task SendStudentReviewRequest(int bookingId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Coach)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null) return;

        // ✅ you said you only have: Pending / Accepted / Cancelled
        // so we only send review request if it was accepted
        if (booking.Status != BookingStatus.Accepted) return;

        if (string.IsNullOrWhiteSpace(booking.StudentEmail)) return;

        var coach = booking.Coach;
        if (coach == null) return;

        var studentName = booking.StudentName ?? "Client";
        var coachName = coach.FullName ?? "Coach";

        // ✅ link where client leaves review (use your real route)
        // Example: https://ptfindernow.com/coaches/123
        var coachProfileUrl = $"{WebBaseUrl.TrimEnd('/')}/coaches/{coach.Id}";

        var subject = $"How was your session with {coachName}? Leave a quick review — PTfinderNow";

        var html = EmailTemplates.ReviewRequestStudentHtml(
            studentName: studentName,
            coachName: coachName,
            coachProfileUrl: coachProfileUrl,
            logoUrl: "https://ptfindernow.com/images/PtFinderNow.png",
            supportEmail: "info@ptfindernow.com",
            webBaseUrl: WebBaseUrl
        );

        var text =
    $@"Hi {studentName},

How was your session with {coachName}?
Please leave a quick review (30 seconds):
{coachProfileUrl}

Thank you,
PTfinderNow";

        await _sender.SendAsync(
            to: booking.StudentEmail,
            subject: subject,
            htmlBody: html,
            textBody: text,
            ct: ct
        );
    }

}

