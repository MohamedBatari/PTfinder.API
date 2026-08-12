using System;
using System.Net;

namespace PTfinder.API.Services.Emails
{
    public static class EmailTemplates
    {
        private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        // ✅ One shared wrapper for ALL emails (mobile friendly + logo)
        private static string Wrap(
            string title,
            string logoUrl,
            string badgeText,
            string bodyHtml,
            string supportEmail = "info@ptfindernow.com",
            string webBaseUrl = "https://ptfindernow.com")
        {
            // ✅ Force logo everywhere (fallback)
            if (string.IsNullOrWhiteSpace(logoUrl))
                logoUrl = "https://ptfindernow.com/images/PtFinderNow.png";

            // NOTE: Use max-width + width:100% to show logo on mobile
            return $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>{E(title)}</title>
</head>
<body style=""margin:0;padding:0;background:#f6f9fc;font-family:Arial,Helvetica,sans-serif;color:#0f172a;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f6f9fc;padding:20px 10px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0""
               style=""width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 24px rgba(15,23,42,.08);"">

          <!-- Header -->
          <tr>
            <td style=""background:linear-gradient(135deg,#2563eb,#0ea5e9);padding:18px 16px;text-align:center;"">
              <img src=""{E(logoUrl)}"" alt=""PTfinderNow""
                   style=""display:block;margin:0 auto;border:0;outline:none;text-decoration:none;
                          max-width:180px;width:100%;height:auto;"" />
              <div style=""margin-top:10px;font-size:12px;color:rgba(255,255,255,.95);font-weight:700;"">
                {E(badgeText)}
              </div>
            </td>
          </tr>

          <!-- Body -->
          <tr>
            <td style=""padding:22px 16px 8px 16px;"">
              {bodyHtml}
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding:14px 16px 18px 16px;"">
              <div style=""border-top:1px solid #e2e8f0;padding-top:12px;font-size:11px;color:#94a3b8;line-height:1.7;"">
                This is an automated message from PTfinderNow.<br/>
                Need help? Contact
                <a href=""mailto:{E(supportEmail)}"" style=""color:#2563eb;text-decoration:none;font-weight:800;"">{E(supportEmail)}</a>.<br/>
                © {DateTime.UtcNow:yyyy} PTfinderNow. All rights reserved.<br/>
                <span style=""color:#94a3b8;"">{E(webBaseUrl)}</span>
              </div>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
        }

        private static string InfoRow(string label, string value)
        {
            return $@"
<tr>
  <td style=""padding:10px 12px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#334155;width:38%;"">
    {E(label)}
  </td>
  <td style=""padding:10px 12px;border-bottom:1px solid #e2e8f0;font-size:13px;color:#0f172a;font-weight:800;text-align:right;"">
    {E(value)}
  </td>
</tr>";
        }

        private static string Button(string url, string text)
        {
            return $@"
<div style=""margin:18px 0 10px 0;text-align:center;"">
  <a href=""{E(url)}""
     style=""display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;font-weight:900;
            font-size:14px;padding:12px 18px;border-radius:12px;"">
    {E(text)}
  </a>
</div>
<div style=""text-align:center;font-size:12px;color:#64748b;margin-top:6px;"">
  If the button doesn’t work, copy and paste this link:<br/>
  <span style=""color:#2563eb;"">{E(url)}</span>
</div>";
        }

        public static string PasswordResetHtml(
            string firstName,
            string resetUrl,
            int expiresMinutes,
            string logoUrl,
            string supportEmail = "info@ptfindernow.com",
            string webBaseUrl = "https://ptfindernow.com")
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">Hi {E(firstName)},</div>

<div style=""font-size:14px;line-height:1.7;color:#334155;"">
  We received a request to reset your <strong>PTfinderNow</strong> password.
</div>

{Button(resetUrl, "Reset your password →")}

<div style=""margin-top:16px;font-size:13px;line-height:1.8;color:#475569;"">
  This link expires in <strong>{expiresMinutes} minutes</strong> and stops working after your password is changed.<br/>
  If you did not request this reset, you can safely ignore this email.
</div>";

            return Wrap(
                title: "Reset your PTfinderNow password",
                logoUrl: logoUrl,
                badgeText: "Password reset",
                bodyHtml: body,
                supportEmail: supportEmail,
                webBaseUrl: webBaseUrl
            );
        }

        // ✅ Welcome Coach Email (UPDATED: add Tips + Thanks Gift note)
        public static string WelcomeCoachHtml(
            string firstName,
            string premiumUntil,
            string categoryName,
            string specialityName,
            string logoUrl,
            string dashboardUrl = "https://ptfindernow.com/dashboard",
            string supportEmail = "info@ptfindernow.com")
        {
            var body = $@"
<div style=""font-size:18px;font-weight:900;margin-bottom:10px;"">Hi {E(firstName)},</div>

<div style=""font-size:14px;line-height:1.7;color:#334155;margin-bottom:12px;"">
  Welcome to <strong>PTfinderNow</strong> 👋<br/>
  We’re excited to have you onboard.
</div>

<div style=""font-size:14px;line-height:1.7;color:#334155;"">
  Your expert profile is now live on <strong>PTfinderNow</strong>, a platform designed to help professionals like you
  attract clients, manage bookings, and grow visibility — all in one place.
</div>

<div style=""margin:16px 0 8px 0;"">
  <span style=""display:inline-block;background:#ecfeff;color:#155e75;border:1px solid #a5f3fc;
    padding:10px 12px;border-radius:12px;font-size:13px;font-weight:900;"">
    ✅ Premium Early Access active until: {E(premiumUntil)}
  </span>
</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin-top:14px;border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;"">
  {InfoRow("Category", categoryName)}
  {InfoRow("Speciality", specialityName)}
</table>

<div style=""margin-top:18px;font-size:14px;font-weight:900;color:#0f172a;"">🎯 What PTfinderNow does for you:</div>
<div style=""margin-top:10px;font-size:13px;line-height:1.9;color:#475569"">
  • Makes your profile publicly discoverable by new clients<br/>
  • Shows your availability so clients can book faster<br/>
  • Displays your contact details (WhatsApp, phone, email) for direct inquiries<br/>
  • Collects reviews to build trust and credibility<br/>
  • Centralizes your gallery, pricing, and professional info<br/>
  • Saves you time managing requests and schedules
</div>

<div style=""margin-top:16px;font-size:14px;font-weight:900;color:#0f172a;"">📌 Recommended next steps:</div>
<div style=""margin-top:8px;font-size:13px;line-height:1.9;color:#475569"">
  1) Upload a high-quality profile photo<br/>
  2) Add your availability for this week<br/>
  3) Add 2–3 reviews from previous clients<br/>
  4) Share your PTfinderNow profile link on WhatsApp or Instagram
</div>

<div style=""margin-top:16px;font-size:14px;font-weight:900;color:#0f172a;"">🎁 Tips & Thanks Gift (when Stripe is approved)</div>
<div style=""margin-top:8px;font-size:13px;line-height:1.9;color:#475569"">
  Once your <strong>Stripe</strong> account is approved and connected, clients will be able to send you <strong>Tips / Thanks Gifts</strong> directly through PTfinderNow.<br/>
  This is optional, but it’s a great way to earn extra rewards from happy clients.
</div>

{Button(dashboardUrl, "Go to your dashboard →")}

<div style=""margin-top:14px;font-size:13px;color:#334155;line-height:1.8;"">
  Best regards,<br/>
  <strong>The PTfinderNow Team</strong>
</div>";

            return Wrap(
                title: "Welcome to PTfinderNow",
                logoUrl: logoUrl,
                badgeText: "Expert profile activated",
                bodyHtml: body,
                supportEmail: supportEmail
            );
        }

        // ✅ OTP Verification HTML (KEEP)
        public static string VerifyOtpHtml(
            string firstName,
            string code,
            int expiresMinutes,
            string logoUrl,
            string supportEmail = "info@ptfindernow.com",
            string webBaseUrl = "https://ptfindernow.com")
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">Hi {E(firstName)},</div>

<div style=""font-size:14px;line-height:1.7;color:#334155;"">
  Use the code below to verify your email on <strong>PTfinderNow</strong>.
</div>

<div style=""margin:18px 0 12px 0;text-align:center;"">
  <div style=""display:inline-block;background:#0f172a;color:#ffffff;
              font-weight:900;font-size:28px;letter-spacing:6px;
              padding:14px 18px;border-radius:14px;"">
    {E(code)}
  </div>
</div>

<div style=""font-size:13px;line-height:1.8;color:#475569;text-align:center;"">
  This code expires in <strong>{expiresMinutes}</strong> minutes.<br/>
  Do not share this code with anyone.
</div>";

            return Wrap(
                title: "Your verification code",
                logoUrl: logoUrl,
                badgeText: "Email verification",
                bodyHtml: body,
                supportEmail: supportEmail,
                webBaseUrl: webBaseUrl
            );
        }

        // ✅ Booking request → Coach (HAS manage button)
        public static string BookingRequestCoachHtml(
            string coachName,
            string studentName,
            string studentEmail,
            string studentPhone,
            string whenText,
            string timeSlot,
            string manageUrl,
            string logoUrl)
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">New booking request</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;"">
  {InfoRow("Coach", coachName)}
  {InfoRow("Client", studentName)}
  {InfoRow("Client Email", studentEmail)}
  {InfoRow("Client Phone", studentPhone)}
  {InfoRow("Date/Time", whenText)}
  {InfoRow("Time Slot", timeSlot)}
</table>

<div style=""margin-top:14px;font-size:13px;line-height:1.7;color:#475569;"">
  Please confirm or decline this request from your dashboard.
</div>

{Button(manageUrl, "Open booking in dashboard →")}";

            return Wrap(
                title: "New booking request",
                logoUrl: logoUrl,
                badgeText: "Action required",
                bodyHtml: body
            );
        }

        // Client conversation lead → Coach. This notification is intentionally
        // independent of the coach subscription: a lead must always be visible
        // in the dashboard and the coach should never miss it.
        public static string ConversationLeadCoachHtml(
            string coachName,
            string clientName,
            string clientEmail,
            string message,
            string inboxUrl,
            string logoUrl)
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">New client lead</div>

<div style=""font-size:13px;line-height:1.7;color:#475569;"">
  Hi {E(coachName)},<br/>
  <strong>{E(clientName)}</strong> sent you a private message on PTfinderNow.
</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;margin-top:14px;"">
  {InfoRow("Client", clientName)}
  {InfoRow("Email", clientEmail)}
</table>

<div style=""margin-top:14px;background:#f8fafc;border-left:4px solid #2563eb;border-radius:10px;padding:12px;font-size:13px;line-height:1.7;color:#334155;"">
  {E(message)}
</div>

{Button(inboxUrl, "Open client inbox →")}";

            return Wrap(
                title: "New client lead",
                logoUrl: logoUrl,
                badgeText: "New lead",
                bodyHtml: body
            );
        }

        // ✅ Booking request → Student (NO manage link)
        public static string BookingRequestStudentHtml(
            string studentName,
            string coachName,
            string whenText,
            string timeSlot,
            string logoUrl)
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">Your request was sent ✅</div>

<div style=""font-size:13px;line-height:1.7;color:#475569;"">
  Hi {E(studentName)},<br/>
  We sent your booking request to <strong>{E(coachName)}</strong>.
  You will receive an email once confirms or declines.
</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;margin-top:12px;"">
  {InfoRow("Name", coachName)}
  {InfoRow("Requested time", whenText)}
  {InfoRow("Time Slot", timeSlot)}
</table>

<div style=""margin-top:12px;font-size:12px;line-height:1.7;color:#64748b;"">
  Note: Clients cannot manage bookings from the dashboard.
</div>";

            return Wrap(
                title: "Booking request received",
                logoUrl: logoUrl,
                badgeText: "Request sent",
                bodyHtml: body
            );
        }

        // ✅ Booking accepted → Student (NO manage link)
        public static string BookingAcceptedStudentHtml(
            string studentName,
            string coachName,
            string whenText,
            string timeSlot,
            string logoUrl)
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;color:#16a34a;"">Booking confirmed ✅</div>

<div style=""font-size:13px;line-height:1.7;color:#475569;"">
  Hi {E(studentName)},<br/>
  Your booking has been confirmed by <strong>{E(coachName)}</strong>.
</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;margin-top:12px;"">
  {InfoRow("Name", coachName)}
  {InfoRow("Date/Time", whenText)}
  {InfoRow("Time Slot", timeSlot)}
</table>

<div style=""margin-top:12px;font-size:12px;line-height:1.7;color:#64748b;"">
  Note: Clients cannot manage bookings from the dashboard.
</div>";

            return Wrap(
                title: "Booking confirmed",
                logoUrl: logoUrl,
                badgeText: "Confirmed",
                bodyHtml: body
            );
        }

        // ✅ Booking declined → Student (NO manage link)
        public static string BookingDeclinedStudentHtml(
            string studentName,
            string coachName,
            string whenText,
            string timeSlot,
            string searchUrl,
            string logoUrl)
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;color:#ef4444;"">Booking declined ❌</div>

<div style=""font-size:13px;line-height:1.7;color:#475569;"">
  Hi {E(studentName)},<br/>
  Unfortunately, <strong>{E(coachName)}</strong> declined your request.
</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;margin-top:12px;"">
  {InfoRow("Name", coachName)}
  {InfoRow("Requested time", whenText)}
  {InfoRow("Time Slot", timeSlot)}
</table>

<div style=""margin-top:14px;text-align:center;font-size:13px;"">
  <a href=""{E(searchUrl)}"" style=""color:#2563eb;text-decoration:none;font-weight:900;"">
    Search for another coach →
  </a>
</div>";

            return Wrap(
                title: "Booking declined",
                logoUrl: logoUrl,
                badgeText: "Not confirmed",
                bodyHtml: body
            );
        }

        // ✅ Reminder → Student (24h / 2h)
        public static string BookingReminderStudentHtml(
            string studentName,
            string coachName,
            string whenText,
            string timeSlot,
            int hoursBefore,
            string logoUrl)
        {
            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">⏰ Session reminder</div>

<div style=""font-size:13px;line-height:1.7;color:#475569;"">
  Hi {E(studentName)},<br/>
  This is a reminder that your session with <strong>{E(coachName)}</strong> starts in <strong>{hoursBefore} hours</strong>.
</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""border:1px solid #e2e8f0;border-radius:14px;overflow:hidden;margin-top:12px;"">
  {InfoRow("Name", coachName)}
  {InfoRow("Date/Time", whenText)}
  {InfoRow("Time Slot", timeSlot)}
</table>";

            return Wrap(
                title: "Session reminder",
                logoUrl: logoUrl,
                badgeText: "Reminder",
                bodyHtml: body
            );
        }

        // ✅ Review request (Student → leave review on coach profile)
        public static string ReviewRequestStudentHtml(
            string studentName,
            string coachName,
            string coachProfileUrl,
            string logoUrl,
            string supportEmail = "info@ptfindernow.com",
            string webBaseUrl = "https://ptfindernow.com")
        {
            if (string.IsNullOrWhiteSpace(coachProfileUrl))
                coachProfileUrl = webBaseUrl.TrimEnd('/') + "/";

            var body = $@"
<div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">
  How was your session? ⭐
</div>

<div style=""font-size:13px;line-height:1.75;color:#475569;"">
  Hi {E(studentName)},<br/>
  We hope your session with <strong>{E(coachName)}</strong> went great.<br/>
  Would you take <strong>30 seconds</strong> to leave a quick review? Your feedback helps others choose the right expert.
</div>

<div style=""margin:16px 0 10px 0;background:#f8fafc;border:1px solid #e2e8f0;border-radius:14px;padding:12px 12px;"">
  <div style=""font-size:13px;font-weight:900;color:#0f172a;margin-bottom:6px;"">Why reviews matter</div>
  <div style=""font-size:12px;line-height:1.8;color:#64748b;"">
    • Helps other clients trust this expert<br/>
    • Supports the coach to grow on PTfinderNow<br/>
    • Improves overall quality of the platform
  </div>
</div>

{Button(coachProfileUrl, "Leave a review →")}

<div style=""margin-top:14px;font-size:12px;line-height:1.7;color:#64748b;text-align:center;"">
  Thank you for helping the PTfinderNow community.
</div>";

            return Wrap(
                title: "Please leave a review",
                logoUrl: logoUrl,
                badgeText: "Review request",
                bodyHtml: body,
                supportEmail: supportEmail,
                webBaseUrl: webBaseUrl
            );
        }
    

}
}
