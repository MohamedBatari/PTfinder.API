using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PTfinder.API.Services;
using PTfinder.API.DATA.DTO;
using PTfinder.API.Helpers;
using PTfinder.API.Settings;

[ApiController]
[Route("api/[controller]")]
public class EmailsController : ControllerBase
{
    private readonly IEmailSender _sender;
    private readonly SmtpSettings _smtp;

    public EmailsController(IEmailSender sender, IOptions<SmtpSettings> smtp)
    {
        _sender = sender;
        _smtp = smtp.Value;
    }

    // Deliverability headers per flow
    private Dictionary<string, string> FlowHeaders(string flow) => new()
    {
        { "List-Unsubscribe", "<mailto:unsubscribe@ptfindernow.com>, <https://ptfindernow.com/unsubscribe>" },
        { "List-Unsubscribe-Post", "List-Unsubscribe=One-Click" },
        { "Auto-Submitted", "auto-generated" },
        { "X-Auto-Response-Suppress", "All" },
        { "Feedback-ID", $"ptn-tx:{flow}:ptfindernow" } // keep if you use Feedback-ID
        // Note: X-SES-CONFIGURATION-SET is added automatically in SmtpEmailSender from _smtp.ConfigSet
    };

    private static IEnumerable<(string Name, string Value)> Tags(params (string, string)[] xs)
    {
        var list = new List<(string, string)>(xs)
        {
            ("type","transactional"),
            ("channel","email"),
            ("lang","en")
        };
        return list;
    }

    // ── Verification (link)
    [HttpPost("verify-link")]
    public async Task<IActionResult> VerifyLink([FromBody] VerifyLinkEmailDto dto, CancellationToken ct)
    {
        var subject = "Verify your email for PTfinderNow";
        var text =
$@"Hi {dto.FirstName},

Please verify your email to finish setting up your PTfinderNow account:
{dto.VerifyUrl}

This link expires in {dto.ExpiresMinutes} minutes.
If you didn’t sign up, you can ignore this email.

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("verify-link"),
            tags: Tags(("flow", "verify-link")),
            fromOverride: _smtp.FromAddresses.Verification);
        return Ok(new { sent = true });
    }

    // ── Verification (OTP)
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpEmailDto dto, CancellationToken ct)
    {
        var subject = $"Your PTfinderNow verification code: {dto.Code}";
        var text =
$@"Hi {dto.FirstName},

Use this code to verify your email:
{dto.Code}

Do not share this code. It expires in {dto.ExpiresMinutes} minutes.
If you didn’t request this, ignore this email.

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("verify-otp"),
            tags: Tags(("flow", "verify-otp")),
            fromOverride: _smtp.FromAddresses.Verification);
        return Ok(new { sent = true });
    }

    // ── Email verified
    [HttpPost("email-verified")]
    public async Task<IActionResult> EmailVerified([FromBody] EmailVerifiedEmailDto dto, CancellationToken ct)
    {
        var subject = "Your email is verified — PTfinderNow";
        var text =
$@"Congrats {dto.FirstName}! 🎉

Your email is now verified. You can start booking sessions right away.

Dashboard:
{dto.DashboardUrl}

Need help? Just reply to this email.

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("email-verified"),
            tags: Tags(("flow", "email-verified")),
            fromOverride: _smtp.FromAddresses.Welcome);
        return Ok(new { sent = true });
    }

    // ── Welcome
    [HttpPost("welcome")]
    public async Task<IActionResult> Welcome([FromBody] WelcomeEmailDto dto, CancellationToken ct)
    {
        var subject = $"Welcome to PTfinderNow, {dto.FirstName}";
        var text =
$@"Welcome aboard, {dto.FirstName}! 🙌

What’s next:
• Browse coaches that match your goals
• Request a session
• Manage your bookings and reminders

Dashboard:
{dto.DashboardUrl}

Questions? Reply to this email — we’re here to help.

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("welcome"),
            tags: Tags(("flow", "welcome")),
            fromOverride: _smtp.FromAddresses.Welcome);
        return Ok(new { sent = true });
    }

    // ── Reset password
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordEmailDto dto, CancellationToken ct)
    {
        var subject = "Reset your PTfinderNow password";
        var text =
$@"We received a request to reset your PTfinderNow password for {dto.Email}.

Reset your password:
{dto.ResetLink}

This link expires in {dto.ExpiresMinutes} minutes.
If you didn’t request a reset, ignore this — your account is still secure.

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("reset-password"),
            tags: Tags(("flow", "reset-password")),
            fromOverride: _smtp.FromAddresses.Default);
        return Ok(new { sent = true });
    }

    // ── Booking: request → PT
    [HttpPost("booking/request-pt")]
    public async Task<IActionResult> BookingRequestPt([FromBody] BookingRequestPtEmailDto dto, CancellationToken ct)
    {
        var subject = $"New booking request from {dto.ClientName} — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"You have a new booking request.

Client: {dto.ClientName}
Service: {dto.ServiceName}
Date/Time: {when}
Location: {dto.Location}
Duration: {dto.DurationMinutes} minutes
Price: {dto.Price}

Confirm: {dto.ConfirmUrl}
Decline: {dto.DeclineUrl}

Please respond within {dto.ResponseSlaHours} hours.

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("booking-request-pt"),
            tags: Tags(("flow", "booking-request-pt")),
            fromOverride: _smtp.FromAddresses.Booking);
        return Ok(new { sent = true });
    }

    // ── Booking: request acknowledgement → Client
    [HttpPost("booking/request-client")]
    public async Task<IActionResult> BookingRequestClient([FromBody] BookingRequestClientEmailDto dto, CancellationToken ct)
    {
        var subject = $"We sent your request to {dto.PtName} — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Your booking request has been sent to {dto.PtName}.

Service: {dto.ServiceName}
Date/Time: {when}
Location: {dto.Location}

We’ll email you as soon as {dto.PtName} confirms or proposes a new time.
You can manage your request here:
{dto.ManageUrl}

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("booking-request-client"),
            tags: Tags(("flow", "booking-request-client")),
            fromOverride: _smtp.FromAddresses.Booking);
        return Ok(new { sent = true });
    }

    // ── Booking: confirmed by PT → notify Client (attach .ics)
    [HttpPost("booking/confirmed-client")]
    public async Task<IActionResult> BookingConfirmedClient([FromBody] BookingConfirmedClientEmailDto dto, CancellationToken ct)
    {
        var subject = $"Booking confirmed — {dto.ServiceName} with {dto.PtName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Your booking is confirmed.

Coach: {dto.PtName}
Service: {dto.ServiceName}
Date/Time: {when}
Location: {dto.Location}
Duration: {dto.DurationMinutes} minutes
Price: {dto.Price}

Manage your booking:
{dto.ManageUrl}
{(dto.AddIcs ? "A calendar invite (.ics) is attached." : "")}

{EmailText.Footer}";

        IEnumerable<(string FileName, string ContentType, byte[] Bytes)>? atts = null;
        if (dto.AddIcs)
        {
            var end = dto.StartsAtLocal.AddMinutes(dto.DurationMinutes);
            var ics = IcsFactory.CreateBookingIcs(
                $"{dto.ServiceName} with {dto.PtName}",
                $"Manage: {dto.ManageUrl}",
                dto.StartsAtLocal.ToUniversalTime(),
                end.ToUniversalTime(),
                dto.Location,
                $"ptn-{dto.BookingId}@ptfindernow.com");
            atts = new[] { ics };
        }

        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("booking-confirmed-client"),
            attachments: atts,
            tags: Tags(("flow", "booking-confirmed-client")),
            fromOverride: _smtp.FromAddresses.Booking);
        return Ok(new { sent = true });
    }

    // ── Booking: confirmation receipt → PT
    [HttpPost("booking/confirmed-pt")]
    public async Task<IActionResult> BookingConfirmedPt([FromBody] BookingConfirmedPtEmailDto dto, CancellationToken ct)
    {
        var subject = $"You confirmed a booking with {dto.ClientName} — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Thanks for confirming.

Client: {dto.ClientName}
Service: {dto.ServiceName}
Date/Time: {when}
Location: {dto.Location}
Duration: {dto.DurationMinutes} minutes
Price: {dto.Price}

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("booking-confirmed-pt"),
            tags: Tags(("flow", "booking-confirmed-pt")),
            fromOverride: _smtp.FromAddresses.Booking);
        return Ok(new { sent = true });
    }

    // ── Booking: cancelled by PT (after confirmation)
    [HttpPost("booking/cancelled-by-pt")]
    public async Task<IActionResult> BookingCancelledByPt([FromBody] BookingCancelledByPtEmailDto dto, CancellationToken ct)
    {
        var subject = $"Booking cancelled by {dto.PtName} — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Your booking was cancelled by {dto.PtName}.

Service: {dto.ServiceName}
Original date/time: {when}
Location: {dto.Location}
Reason: {dto.Reason}

Find another coach:
{dto.SearchUrl}

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("booking-cancelled-by-pt"),
            tags: Tags(("flow", "booking-cancelled-by-pt")),
            fromOverride: _smtp.FromAddresses.Booking);
        return Ok(new { sent = true });
    }

    // ── Booking: request declined by PT (before confirmation)
    [HttpPost("booking/declined-by-pt")]
    public async Task<IActionResult> BookingDeclinedByPt([FromBody] BookingCancelledByPtEmailDto dto, CancellationToken ct)
    {
        var subject = $"{dto.PtName} declined your request — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Your booking request was declined by {dto.PtName}.

Service: {dto.ServiceName}
Requested time: {when}
Location: {dto.Location}
Reason: {dto.Reason}

Find another coach or try a different time:
{dto.SearchUrl}

{EmailText.Footer}";
        await _sender.SendAsync(dto.To, subject, null, text, ct,
            headers: FlowHeaders("booking-declined-by-pt"),
            tags: Tags(("flow", "booking-declined-by-pt")),
            fromOverride: _smtp.FromAddresses.Booking);
        return Ok(new { sent = true });
    }
}

