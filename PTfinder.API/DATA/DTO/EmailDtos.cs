using System.ComponentModel.DataAnnotations;

namespace PTfinder.API.DATA.DTO;

// DATA/DTO/EmailDtos.cs
public sealed class TestEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
}

// Verification – Link
public sealed class VerifyLinkEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    [Required/*, Url*/] public string VerifyUrl { get; set; } = default!;
    [Range(5, 1440)] public int ExpiresMinutes { get; set; } = 15;
}

// Verification – OTP
public sealed class VerifyOtpEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    [Required] public string Code { get; set; } = default!;
    [Range(1, 60)] public int ExpiresMinutes { get; set; } = 10;
}

// Email verified (congrats)
public sealed class EmailVerifiedEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    [Required/*, Url*/] public string DashboardUrl { get; set; } = default!;
}

// Account created / Welcome
public sealed class WelcomeEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    [Required/*, Url*/] public string DashboardUrl { get; set; } = default!;
}

// Booking – client requests PT
public sealed class BookingRequestPtEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!; // PT email
    [Required] public string ClientName { get; set; } = default!;
    [Required] public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    [Required] public string Timezone { get; set; } = "Asia/Dubai";
    [Required] public string Location { get; set; } = "Online";
    [Range(15, 240)] public int DurationMinutes { get; set; } = 60;
    [Required] public string Price { get; set; } = "TBD";
    [Required/*, Url*/] public string ConfirmUrl { get; set; } = default!;
    [Required/*, Url*/] public string DeclineUrl { get; set; } = default!;
    [Range(1, 72)] public int ResponseSlaHours { get; set; } = 12;
}

// Booking – client acknowledgement (“request sent”)
public sealed class BookingRequestClientEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!; // client email
    [Required] public string PtName { get; set; } = default!;
    [Required] public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    [Required] public string Timezone { get; set; } = "Asia/Dubai";
    [Required] public string Location { get; set; } = "Online";
    [Required/*, Url*/] public string ManageUrl { get; set; } = default!;
}

// Booking – confirmed (to client)
public sealed class BookingConfirmedClientEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    [Required] public string PtName { get; set; } = default!;
    [Required] public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    [Required] public string Timezone { get; set; } = "Asia/Dubai";
    [Required] public string Location { get; set; } = "Online";
    [Range(15, 240)] public int DurationMinutes { get; set; } = 60;
    [Required] public string Price { get; set; } = "TBD";
    [Required/*, Url*/] public string ManageUrl { get; set; } = default!;
    public bool AddIcs { get; set; } = true;
    [Required] public string BookingId { get; set; } = default!;
}

// Booking – confirmed (to PT)
public sealed class BookingConfirmedPtEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    [Required] public string ClientName { get; set; } = default!;
    [Required] public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    [Required] public string Timezone { get; set; } = "Asia/Dubai";
    [Required] public string Location { get; set; } = "Online";
    [Range(15, 240)] public int DurationMinutes { get; set; } = 60;
    [Required] public string Price { get; set; } = "TBD";
}

// Booking – cancelled by PT (notify client)
public sealed class BookingCancelledByPtEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    [Required] public string PtName { get; set; } = default!;
    [Required] public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    [Required] public string Timezone { get; set; } = "Asia/Dubai";
    [Required] public string Location { get; set; } = "Online";
    public string Reason { get; set; } = "No reason provided";
    [Required/*, Url*/] public string SearchUrl { get; set; } = default!;
}

// Booking – cancelled by Client (notify PT)
public sealed class BookingCancelledByClientEmailDto
{
    [Required, EmailAddress] public string To { get; set; } = default!;
    [Required] public string ClientName { get; set; } = default!;
    [Required] public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    [Required] public string Timezone { get; set; } = "Asia/Dubai";
    [Required] public string Location { get; set; } = "Online";
    public string Reason { get; set; } = "No reason provided";
}
