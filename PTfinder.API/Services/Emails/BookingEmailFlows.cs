using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.Enums;
using PTfinder.API.Services;

namespace PTfinder.API.Services.Emails;

public interface IBookingEmailFlows
{
    Task SendBookingCreatedEmails(int bookingId, CancellationToken ct = default);
    Task SendBookingAcceptedEmail(int bookingId, CancellationToken ct = default);
    Task SendBookingDeclinedEmail(int bookingId, CancellationToken ct = default);
    Task SendStudentReviewRequest(int bookingId, CancellationToken ct = default);

}

public sealed class BookingEmailFlows : IBookingEmailFlows
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly IConfiguration _cfg;

    public BookingEmailFlows(AppDbContext db, IEmailSender sender, IConfiguration cfg)
    {
        _db = db;
        _sender = sender;
        _cfg = cfg;
    }

    private string WebBaseUrl => _cfg["Web:BaseUrl"] ?? "https://ptfindernow.com";
    private string LogoUrl => _cfg["Branding:LogoUrl"] ?? $"{WebBaseUrl.TrimEnd('/')}/images/PtFinderNow.png";

    private static string DubaiWhenText(DateTime dt) => $"{dt:yyyy-MM-dd HH:mm} (Asia/Dubai)";

    public async Task SendBookingCreatedEmails(int bookingId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking == null) return;

        var coach = await _db.Coaches.AsNoTracking().FirstOrDefaultAsync(c => c.Id == booking.CoachId, ct);
        if (coach == null) return;

        var whenText = DubaiWhenText(booking.BookingDate);
        var coachManageUrl = $"{WebBaseUrl}/dashboard/bookings/{booking.Id}";

        // --- Coach email ---
        var coachHtml = EmailTemplates.BookingRequestCoachHtml(
            coachName: coach.FullName ?? "Coach",
            studentName: booking.StudentName ?? "",
            studentEmail: booking.StudentEmail ?? "",
            studentPhone: booking.StudentPhone ?? "",
            whenText: whenText,
            timeSlot: booking.TimeSlot ?? "",
            manageUrl: coachManageUrl,
            logoUrl: LogoUrl
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

        // --- Student email ---
        var studentHtml = EmailTemplates.BookingRequestStudentHtml(
            studentName: booking.StudentName ?? "Client",
            coachName: coach.FullName ?? "Coach",
            whenText: whenText,
            timeSlot: booking.TimeSlot ?? "",
            logoUrl: LogoUrl
        );

        var studentText =
$@"Your booking request was sent to {coach.FullName}.

Requested time: {whenText}
Time Slot: {booking.TimeSlot}

You'll receive an email when the coach confirms or declines.

— PTfinderNow";

        // ✅ send in parallel
        await Task.WhenAll(
            _sender.SendAsync(
                to: coach.Email,
                subject: $"New booking request — session from {booking.StudentName}",
                htmlBody: coachHtml,
                textBody: coachText,
                ct: ct
            ),
            _sender.SendAsync(
                to: booking.StudentEmail,
                subject: $"Your request was sent to {coach.FullName} — PTfinderNow",
                htmlBody: studentHtml,
                textBody: studentText,
                ct: ct
            )
        );
    }

    public async Task SendBookingAcceptedEmail(int bookingId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking == null) return;

        var coach = await _db.Coaches.AsNoTracking().FirstOrDefaultAsync(c => c.Id == booking.CoachId, ct);
        if (coach == null) return;

        var whenText = DubaiWhenText(booking.BookingDate);

        var html = EmailTemplates.BookingAcceptedStudentHtml(
            studentName: booking.StudentName ?? "Client",
            coachName: coach.FullName ?? "Coach",
            whenText: whenText,
            timeSlot: booking.TimeSlot ?? "",
            logoUrl: LogoUrl
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
            ct: ct
        );
    }

    public async Task SendBookingDeclinedEmail(int bookingId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking == null) return;

        var coach = await _db.Coaches.AsNoTracking().FirstOrDefaultAsync(c => c.Id == booking.CoachId, ct);
        if (coach == null) return;

        var whenText = DubaiWhenText(booking.BookingDate);
        var searchUrl = $"{WebBaseUrl}/coaches/search";

        var html = EmailTemplates.BookingDeclinedStudentHtml(
            studentName: booking.StudentName ?? "Client",
            coachName: coach.FullName ?? "Coach",
            whenText: whenText,
            timeSlot: booking.TimeSlot ?? "",
            searchUrl: searchUrl,
            logoUrl: LogoUrl
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
            ct: ct
        );
    }

    public async Task SendStudentReviewRequest(int bookingId, CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null) return;

        var coach = await _db.Coaches
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == booking.CoachId, ct);

        if (coach == null) return;

        // ✅ only if accepted (optional but recommended)
        if (booking.Status != BookingStatus.Accepted) return;

        var coachProfileUrl = $"{WebBaseUrl.TrimEnd('/')}/coaches/{coach.Id}";

        var html = EmailTemplates.ReviewRequestStudentHtml(
            studentName: booking.StudentName ?? "Client",
            coachName: coach.FullName ?? "Coach",
            coachProfileUrl: coachProfileUrl,
            logoUrl: LogoUrl
        );

        var text =
    $@"How was your session?

Please leave a quick review for {coach.FullName}:
{coachProfileUrl}

— PTfinderNow";

        await _sender.SendAsync(
            to: booking.StudentEmail,
            subject: $"How was your session with {coach.FullName}? Leave a review ⭐",
            htmlBody: html,
            textBody: text,
            ct: ct
        );
    }

}
