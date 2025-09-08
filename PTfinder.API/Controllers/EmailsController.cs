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

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────
    private Dictionary<string, string> FlowHeaders(string flow) => new()
    {
        { "List-Unsubscribe", "<mailto:unsubscribe@ptfindernow.com>, <https://ptfindernow.com/unsubscribe>" },
        { "List-Unsubscribe-Post", "List-Unsubscribe=One-Click" },
        { "Auto-Submitted", "auto-generated" },
        { "X-Auto-Response-Suppress", "All" },
        { "Feedback-ID", $"ptn-tx:{flow}:ptfindernow" }
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

    // pick preferredFrom (Verification/Welcome/Booking/Default), then fall back safely
    private string? SafeFrom(string? preferredFrom)
    {
        var chosen = string.IsNullOrWhiteSpace(preferredFrom)
            ? _smtp?.FromAddresses?.Default
            : preferredFrom;

        return string.IsNullOrWhiteSpace(chosen) ? null : chosen;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Verification (link)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("verify-link")]
    public async Task<IActionResult> VerifyLink([FromBody] VerifyLinkEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = "Verify your email address — PTfinderNow";
        var text =
$@"Hi {dto.FirstName},

Please verify your email address to activate your PTfinderNow account:
{dto.VerifyUrl}

This link expires in {dto.ExpiresMinutes} minutes. If you didn’t create an account, you can safely ignore this message.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("verify-link"),
            tags: Tags(("flow", "verify-link")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Verification)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Verification (OTP)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"Your verification code: {dto.Code} — PTfinderNow";
        var text =
$@"Hi {dto.FirstName},

Use this code to verify your email:
{dto.Code}

Do not share this code. It expires in {dto.ExpiresMinutes} minutes. If you didn’t request this, please ignore it.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("verify-otp"),
            tags: Tags(("flow", "verify-otp")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Verification)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Email verified (generic)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("email-verified")]
    public async Task<IActionResult> EmailVerified([FromBody] EmailVerifiedEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = "Email verified — PTfinderNow";
        var text =
$@"Hi {dto.FirstName},

Your email address has been verified.

You can now access your dashboard:
{dto.DashboardUrl}

If you didn’t request this verification, please let us know by replying to this email.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("email-verified"),
            tags: Tags(("flow", "email-verified")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Welcome)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Email verified (coach-specific)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("email-verified-coach")]
    public async Task<IActionResult> EmailVerifiedCoach([FromBody] EmailVerifiedEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = "Email verified — you’re ready to accept bookings";
        var text =
$@"Hi {dto.FirstName},

Your email is verified and your coach account is active.

Next steps to go live:
• Complete your profile (bio, specialties, pricing, locations)
• Add a high-quality profile photo
• Set your availability so clients can request sessions

Open your dashboard:
{dto.DashboardUrl}

If you need help at any time, just reply to this email.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("email-verified-coach"),
            tags: Tags(("flow", "email-verified-coach")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Welcome)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Welcome (COACH)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("welcome")]
    public async Task<IActionResult> WelcomeCoach([FromBody] WelcomeEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"Welcome to PTfinderNow, {dto.FirstName}";
        var text =
$@"Hi {dto.FirstName},

Welcome aboard. Let’s set you up for success.

Get started:
• Finish your coach profile (specialties, certifications, and a clear bio)
• Add pricing and the services you offer
• Set availability and preferred training locations
• Enable notifications so you never miss a request

Go to your dashboard:
{dto.DashboardUrl}

We’re here to help — reply to this email if you need assistance.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("welcome-coach"),
            tags: Tags(("role", "coach"), ("flow", "welcome-coach")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Welcome)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Welcome (CLIENT)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("welcome-client")]
    public async Task<IActionResult> WelcomeClient([FromBody] WelcomeEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"Welcome to PTfinderNow, {dto.FirstName}";
        var text =
$@"Hi {dto.FirstName},

Welcome to PTfinderNow.

What you can do next:
• Explore verified coaches and training specialties
• Send a booking request that fits your schedule
• Manage your sessions and messages in your dashboard

Open your dashboard:
{dto.DashboardUrl}

Questions? Reply to this email and we’ll help you out.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("welcome-client"),
            tags: Tags(("role", "client"), ("flow", "welcome-client")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Welcome)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Reset password
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = "Reset your password — PTfinderNow";
        var text =
$@"We received a request to reset your PTfinderNow password for {dto.Email}.

Reset your password:
{dto.ResetLink}

This link expires in {dto.ExpiresMinutes} minutes. If you didn’t request a reset, you can ignore this email.

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("reset-password"),
            tags: Tags(("flow", "reset-password")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Default)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Booking: request → PT (coach)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("booking/request-pt")]
    public async Task<IActionResult> BookingRequestPt([FromBody] BookingRequestPtEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"New booking request — {dto.ServiceName} from {dto.ClientName}";
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

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("booking-request-pt"),
            tags: Tags(("flow", "booking-request-pt")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Booking)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Booking: request acknowledgement → Client
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("booking/request-client")]
    public async Task<IActionResult> BookingRequestClient([FromBody] BookingRequestClientEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"We sent your request to {dto.PtName} — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Your booking request has been sent to {dto.PtName}.

Service: {dto.ServiceName}
Requested time: {when}
Location: {dto.Location}

We’ll email you as soon as {dto.PtName} confirms or proposes a new time.
Manage your request:
{dto.ManageUrl}

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("booking-request-client"),
            tags: Tags(("flow", "booking-request-client")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Booking)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Booking: confirmed by PT → notify Client (optional .ics)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("booking/confirmed-client")]
    public async Task<IActionResult> BookingConfirmedClient([FromBody] BookingConfirmedClientEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

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

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("booking-confirmed-client"),
            attachments: atts,
            tags: Tags(("flow", "booking-confirmed-client")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Booking)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Booking: confirmation receipt → PT
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("booking/confirmed-pt")]
    public async Task<IActionResult> BookingConfirmedPt([FromBody] BookingConfirmedPtEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"You confirmed a booking — {dto.ServiceName} for {dto.ClientName}";
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

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("booking-confirmed-pt"),
            tags: Tags(("flow", "booking-confirmed-pt")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Booking)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Booking: cancelled by PT (after confirmation)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("booking/cancelled-by-pt")]
    public async Task<IActionResult> BookingCancelledByPt([FromBody] BookingCancelledByPtEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"Booking cancelled by {dto.PtName} — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Your confirmed booking was cancelled by {dto.PtName}.

Service: {dto.ServiceName}
Original date/time: {when}
Location: {dto.Location}
Reason: {dto.Reason}

Find another coach or reschedule:
{dto.SearchUrl}

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("booking-cancelled-by-pt"),
            tags: Tags(("flow", "booking-cancelled-by-pt")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Booking)
        );
        return Ok(new { sent = true });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Booking: request declined by PT (before confirmation)
    // ─────────────────────────────────────────────────────────────────────────────
    [HttpPost("booking/declined-by-pt")]
    public async Task<IActionResult> BookingDeclinedByPt([FromBody] BookingCancelledByPtEmailDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { error = "Invalid email or parameters." });

        var subject = $"{dto.PtName} declined your request — {dto.ServiceName}";
        var when = $"{dto.StartsAtLocal:yyyy-MM-dd HH:mm} ({dto.Timezone})";
        var text =
$@"Your booking request was declined by {dto.PtName}.

Service: {dto.ServiceName}
Requested time: {when}
Location: {dto.Location}
Reason: {dto.Reason}

You can try a different time or search for another coach:
{dto.SearchUrl}

{EmailText.Footer}";

        await _sender.SendAsync(
            to: dto.To,
            subject: subject,
            htmlBody: null,
            textBody: text,
            ct: ct,
            headers: FlowHeaders("booking-declined-by-pt"),
            tags: Tags(("flow", "booking-declined-by-pt")),
            fromOverride: SafeFrom(_smtp?.FromAddresses?.Booking)
        );
        return Ok(new { sent = true });
    }
}
