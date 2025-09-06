namespace PTfinder.API.DATA.DTO;

// DATA/DTO/EmailDtos.cs
public sealed class TestEmailDto { public string To { get; set; } = default!; }

// Verification – Link
public sealed class VerifyLinkEmailDto
{
    public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    public string VerifyUrl { get; set; } = default!;
    public int ExpiresMinutes { get; set; } = 15;
}

// Verification – OTP
public sealed class VerifyOtpEmailDto
{
    public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    public string Code { get; set; } = default!;
    public int ExpiresMinutes { get; set; } = 10;
}

// Email verified (congrats)
public sealed class EmailVerifiedEmailDto
{
    public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    public string DashboardUrl { get; set; } = default!;
}

// Account created / Welcome
public sealed class WelcomeEmailDto
{
    public string To { get; set; } = default!;
    public string FirstName { get; set; } = "there";
    public string DashboardUrl { get; set; } = default!;
}

// Reset password
public sealed class ResetPasswordEmailDto
{
    public string To { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string ResetLink { get; set; } = default!;
    public int ExpiresMinutes { get; set; } = 30;
}

// Booking – client requests PT
public sealed class BookingRequestPtEmailDto
{
    public string To { get; set; } = default!; // PT email
    public string ClientName { get; set; } = default!;
    public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    public string Timezone { get; set; } = "Asia/Dubai";
    public string Location { get; set; } = "Online";
    public int DurationMinutes { get; set; } = 60;
    public string Price { get; set; } = "TBD";
    public string ConfirmUrl { get; set; } = default!;
    public string DeclineUrl { get; set; } = default!;
    public int ResponseSlaHours { get; set; } = 12;
}

// Booking – client acknowledgement (“request sent”)
public sealed class BookingRequestClientEmailDto
{
    public string To { get; set; } = default!; // client email
    public string PtName { get; set; } = default!;
    public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    public string Timezone { get; set; } = "Asia/Dubai";
    public string Location { get; set; } = "Online";
    public string ManageUrl { get; set; } = default!;
}

// Booking – confirmed (to client)
public sealed class BookingConfirmedClientEmailDto
{
    public string To { get; set; } = default!;
    public string PtName { get; set; } = default!;
    public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    public string Timezone { get; set; } = "Asia/Dubai";
    public string Location { get; set; } = "Online";
    public int DurationMinutes { get; set; } = 60;
    public string Price { get; set; } = "TBD";
    public string ManageUrl { get; set; } = default!;
    public bool AddIcs { get; set; } = true;
    public string BookingId { get; set; } = default!;
}

// Booking – confirmed (to PT)
public sealed class BookingConfirmedPtEmailDto
{
    public string To { get; set; } = default!;
    public string ClientName { get; set; } = default!;
    public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    public string Timezone { get; set; } = "Asia/Dubai";
    public string Location { get; set; } = "Online";
    public int DurationMinutes { get; set; } = 60;
    public string Price { get; set; } = "TBD";
}

// Booking – cancelled by PT (notify client)
public sealed class BookingCancelledByPtEmailDto
{
    public string To { get; set; } = default!;
    public string PtName { get; set; } = default!;
    public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    public string Timezone { get; set; } = "Asia/Dubai";
    public string Location { get; set; } = "Online";
    public string Reason { get; set; } = "No reason provided";
    public string SearchUrl { get; set; } = default!;
}

// Booking – cancelled by Client (notify PT)
public sealed class BookingCancelledByClientEmailDto
{
    public string To { get; set; } = default!;
    public string ClientName { get; set; } = default!;
    public string ServiceName { get; set; } = default!;
    public DateTime StartsAtLocal { get; set; }
    public string Timezone { get; set; } = "Asia/Dubai";
    public string Location { get; set; } = "Online";
    public string Reason { get; set; } = "No reason provided";
}


