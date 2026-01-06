using System;
using System.Net;

namespace PTfinder.API.Services.Emails
{
    public static class EmailTemplates
    {
        // Encode helper
        private static string E(string s) => WebUtility.HtmlEncode(s ?? "");

        // ✅ Welcome Coach Email (your message inside HTML)
        public static string WelcomeCoachHtml(
            string firstName,
            string premiumUntil,
            string categoryName,
            string specialityName,
            string logoUrl,
            string dashboardUrl = "https://ptfindernow.com/dashboard",
            string supportEmail = "info@ptfindernow.com")
        {
            return $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Welcome to PTfinderNow</title>
</head>
<body style=""margin:0;padding:0;background:#f6f9fc;font-family:Arial,Helvetica,sans-serif;color:#0f172a;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f6f9fc;padding:24px 12px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 24px rgba(15,23,42,.08);"">

          <!-- Header -->
          <tr>
            <td style=""background:linear-gradient(135deg,#2563eb,#0ea5e9);padding:22px 18px;text-align:center;"">
              <img src=""{E(logoUrl)}"" alt=""PTfinderNow"" 
                   style=""display:block;margin:0 auto;border:0;outline:none;text-decoration:none;max-width:160px;width:100%;height:auto;"" />
              <div style=""margin-top:10px;font-size:13px;color:rgba(255,255,255,.95);"">
                Expert profile activated
              </div>
            </td>
          </tr>

          <!-- Body -->
          <tr>
            <td style=""padding:26px 18px 8px 18px;"">
              <div style=""font-size:18px;font-weight:800;margin-bottom:10px;"">
                Hi {E(firstName)},
              </div>

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
                  padding:10px 12px;border-radius:12px;font-size:13px;font-weight:800;"">
                  ✅ Premium Early Access active until: {E(premiumUntil)}
                </span>
              </div>

              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin-top:14px;border:1px solid #e2e8f0;border-radius:14px;"">
                <tr>
                  <td style=""padding:14px;font-size:13px;color:#0f172a;"">
                    <div style=""font-weight:800;margin-bottom:6px;"">Your profile setup</div>
                    <div style=""color:#475569;line-height:1.7"">
                      • Category: <strong>{E(categoryName)}</strong><br/>
                      • Speciality: <strong>{E(specialityName)}</strong>
                    </div>
                  </td>
                </tr>
              </table>

              <div style=""margin-top:18px;font-size:14px;font-weight:900;color:#0f172a;"">
                🎯 What PTfinderNow does for you:
              </div>

              <div style=""margin-top:10px;font-size:13px;line-height:1.9;color:#475569"">
                • Makes your profile publicly discoverable by new clients<br/>
                • Shows your availability so clients can book faster<br/>
                • Displays your contact details (WhatsApp, phone, email) for direct inquiries<br/>
                • Collects reviews to build trust and credibility<br/>
                • Centralizes your gallery, pricing, and professional info<br/>
                • Saves you time managing requests and schedules
              </div>

              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin-top:16px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:14px;"">
                <tr>
                  <td style=""padding:14px;"">
                    <div style=""font-size:14px;font-weight:900;color:#0f172a;"">
                      ⭐ Use PTfinderNow with your existing clients
                    </div>
                    <div style=""margin-top:6px;font-size:13px;line-height:1.8;color:#475569;"">
                      PTfinderNow isn’t only for new clients.<br/><br/>
                      You can also:<br/>
                      • Share your profile link with previous clients<br/>
                      • Ask them to leave a short review<br/>
                      • Use your availability calendar instead of back-and-forth messages<br/>
                      • Keep everything professional and organized in one place<br/><br/>
                      Many experts see faster growth when they add just <strong>2–3 reviews from past clients</strong>.
                    </div>
                  </td>
                </tr>
              </table>

              <div style=""margin-top:16px;font-size:14px;font-weight:900;color:#0f172a;"">
                🚀 Your Early Access Benefits
              </div>
              <div style=""margin-top:8px;font-size:13px;line-height:1.9;color:#475569"">
                As part of our early access program, your account currently includes premium features at no cost:<br/>
                • Priority visibility<br/>
                • Full calendar &amp; booking tools<br/>
                • Gallery &amp; reviews<br/>
                • Advanced profile features<br/>
                • Access to future monetization tools (Thanks Gifts)
              </div>

              <div style=""margin-top:16px;font-size:14px;font-weight:900;color:#0f172a;"">
                📌 Recommended next steps:
              </div>
              <div style=""margin-top:8px;font-size:13px;line-height:1.9;color:#475569"">
                1) Upload a high-quality profile photo<br/>
                2) Add your availability for this week<br/>
                3) Add 2–3 reviews from previous clients<br/>
                4) Share your PTfinderNow profile link on WhatsApp or Instagram
              </div>

              <div style=""margin:20px 0 14px 0;text-align:center;"">
                <a href=""{E(dashboardUrl)}""
                   style=""display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;
                          font-weight:900;font-size:14px;padding:12px 18px;border-radius:12px;"">
                  Go to your dashboard →
                </a>
              </div>

              <div style=""text-align:center;font-size:12px;color:#64748b;margin-top:8px;"">
                If the button doesn’t work, copy and paste this link:<br/>
                <span style=""color:#2563eb;"">{E(dashboardUrl)}</span>
              </div>

              <div style=""margin-top:14px;font-size:13px;color:#475569;line-height:1.8;"">
                If you need help or have questions, our team is here for you:<br/>
                📧 <a href=""mailto:{E(supportEmail)}"" style=""color:#2563eb;text-decoration:none;font-weight:800;"">{E(supportEmail)}</a>
              </div>

              <div style=""margin-top:16px;font-size:13px;color:#334155;line-height:1.8;"">
                We’re happy to have you with us and look forward to seeing you grow on PTfinderNow.<br/><br/>
                Best regards,<br/>
                <strong>The PTfinderNow Team</strong>
              </div>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding:16px 18px 22px 18px;"">
              <div style=""border-top:1px solid #e2e8f0;margin-top:10px;padding-top:12px;font-size:11px;color:#94a3b8;line-height:1.7;"">
                You are receiving this email because you registered an expert account on PTfinderNow.<br/>
                © {DateTime.UtcNow:yyyy} PTfinderNow. All rights reserved.
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

        // ✅ OTP Verification HTML (NEW)
        public static string VerifyOtpHtml(
            string firstName,
            string code,
            int expiresMinutes,
            string logoUrl,
            string supportEmail = "info@ptfindernow.com",
            string webBaseUrl = "https://ptfindernow.com")
        {
            return $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Your verification code</title>
</head>
<body style=""margin:0;padding:0;background:#f6f9fc;font-family:Arial,Helvetica,sans-serif;color:#0f172a;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f6f9fc;padding:24px 12px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0""
               style=""width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 24px rgba(15,23,42,.08);"">

          <!-- Header -->
          <tr>
            <td style=""background:linear-gradient(135deg,#2563eb,#0ea5e9);padding:22px 18px;text-align:center;"">
              <img src=""{E(logoUrl)}"" alt=""PTfinderNow""
                   style=""display:block;margin:0 auto;border:0;outline:none;text-decoration:none;max-width:160px;width:100%;height:auto;"" />
              <div style=""margin-top:10px;font-size:13px;color:rgba(255,255,255,.95);"">
                Email verification
              </div>
            </td>
          </tr>

          <!-- Body -->
          <tr>
            <td style=""padding:24px 18px 8px 18px;"">
              <div style=""font-size:16px;font-weight:900;margin-bottom:10px;"">
                Hi {E(firstName)},
              </div>

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
              </div>

              <div style=""margin-top:18px;font-size:12px;color:#64748b;line-height:1.7;"">
                If you didn’t request this, you can ignore this email.<br/>
                Need help? Contact us at
                <a href=""mailto:{E(supportEmail)}"" style=""color:#2563eb;text-decoration:none;font-weight:800;"">{E(supportEmail)}</a>.
              </div>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding:16px 18px 22px 18px;"">
              <div style=""border-top:1px solid #e2e8f0;margin-top:10px;padding-top:12px;font-size:11px;color:#94a3b8;line-height:1.7;"">
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
    }
}

