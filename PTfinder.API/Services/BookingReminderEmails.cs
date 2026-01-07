using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.Enums;
using PTfinder.API.Services.Emails;

namespace PTfinder.API.Services;

public interface IBookingReminderEmails
{
    Task SendStudentReminder(int bookingId, int hoursBefore, CancellationToken ct = default);
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

    // ✅ Keep this consistent with BookingController
    private string LogoUrl =>
        _cfg["Branding:LogoUrl"] ?? $"{WebBaseUrl}/logo.png";

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

        // Basic guard
        if (string.IsNullOrWhiteSpace(booking.StudentEmail)) return;

        var whenText = $"{booking.BookingDate:yyyy-MM-dd HH:mm} (Asia/Dubai)";

        var subject = hoursBefore >= 24
            ? $"Reminder: your session tomorrow with {coach.FullName} — PTfinderNow"
            : $"Reminder: your session in {hoursBefore} hours with {coach.FullName} — PTfinderNow";

        // ✅ Use EmailTemplates (no EmailLayout anymore)
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
}


