using System;
using System.Net;

namespace PTfinder.API.Services.Emails
{
    public static class EmailTemplates
    {
        public static string WelcomeCoachHtml(
            string firstName,
            string premiumUntil,
            string categoryName,
            string specialityName,
            string logoUrl,
            string dashboardUrl = "https://ptfindernow.com/dashboard",
            string supportEmail = "info@ptfindernow.com")
        {
            static string E(string s) => WebUtility.HtmlEncode(s ?? "");

            return $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>Welcome to PTfinderNow</title>
</head>
<body style=""margin:0;padding:0;background:#f6f9fc;font-family:Arial,Helvetica,sans-serif;color:#0f172a;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f6f9fc;padding:24px 12px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""width:100%;max-width:600px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 8px 24px rgba(15,23,42,.08);"">

          <!-- Header (Logo) -->
          <tr>
            <td style=""background:linear-gradient(135deg,#2563eb,#0ea5e9);padding:24px 28px;"">
              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"">
                <tr>
                  <td style=""vertical-align:middle;"">
                    <img src=""{E(logoUrl)}"" alt=""PTfinderNow"" width=""120""
                         style=""display:block;border:0;outline:none;text-decoration:none;"" />
                  </td>
                  <td align=""right"" style=""font-size:13px;color:rgba(255,255,255,.9);"">
                    Expert profile activated
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- Body -->
          <tr>
            <td style=""padding:28px 28px 8px 28px;"">
              <div style=""font-size:20px;font-weight:700;margin-bottom:10px;"">
                Welcome, {E(firstName)} 👋
              </div>

              <div style=""font-size:14px;line-height:1.7;color:#334155;"">
                Your expert profile is now live on <strong>PTfinderNow</strong> — a marketplace that helps experts attract clients,
                manage bookings, and grow visibility in one place.
              </div>

              <div style=""margin:18px 0 8px 0;"">
                <span style=""display:inline-block;background:#ecfeff;color:#155e75;border:1px solid #a5f3fc;
                  padding:10px 12px;border-radius:12px;font-size:13px;font-weight:700;"">
                  ✅ Premium Early Access active until: {E(premiumUntil)}
                </span>
              </div>

              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin-top:16px;border:1px solid #e2e8f0;border-radius:14px;"">
                <tr>
                  <td style=""padding:14px 14px;font-size:13px;color:#0f172a;"">
                    <div style=""font-weight:700;margin-bottom:6px;"">Your profile setup</div>
                    <div style=""color:#475569;line-height:1.7"">
                      • Category: <strong>{E(categoryName)}</strong><br/>
                      • Speciality: <strong>{E(specialityName)}</strong>
                    </div>
                  </td>
                </tr>
              </table>

              <div style=""margin-top:18px;font-size:14px;font-weight:700;color:#0f172a;"">What PTfinderNow does for you</div>
              <div style=""margin-top:10px;font-size:13px;line-height:1.8;color:#475569"">
                ✅ Visibility to new clients<br/>
                ✅ Availability slots so clients book faster<br/>
                ✅ Public professional profile (photo, description, gallery, reviews)<br/>
                ✅ Direct inquiries (WhatsApp, phone, email) depending on platform settings<br/>
                ✅ Reviews to build trust and credibility<br/>
                ✅ One place to manage requests and bookings
              </div>

              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin-top:18px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:14px;"">
                <tr>
                  <td style=""padding:14px 14px;"">
                    <div style=""font-size:14px;font-weight:800;color:#0f172a;"">⭐ Use PTfinderNow with your previous clients</div>
                    <div style=""margin-top:6px;font-size:13px;line-height:1.8;color:#475569;"">
                      Share your PTfinderNow profile link with past clients and ask them to leave a short review.
                      Many experts grow faster after adding just <strong>2–3 reviews</strong>.
                    </div>
                  </td>
                </tr>
              </table>

              <div style=""margin:22px 0 18px 0;text-align:center;"">
                <a href=""{E(dashboardUrl)}""
                   style=""display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;
                          font-weight:800;font-size:14px;padding:12px 18px;border-radius:12px;"">
                  Go to Dashboard →
                </a>
              </div>

              <div style=""text-align:center;font-size:12px;color:#64748b;margin-top:8px;"">
                If the button doesn’t work, copy and paste this link:<br/>
                <span style=""color:#2563eb;"">{E(dashboardUrl)}</span>
              </div>

              <div style=""margin-top:14px;font-size:13px;color:#475569;line-height:1.8;"">
                Need help? Contact us at
                <a href=""mailto:{E(supportEmail)}"" style=""color:#2563eb;text-decoration:none;font-weight:700;"">{E(supportEmail)}</a>.
              </div>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding:18px 28px 26px 28px;"">
              <div style=""border-top:1px solid #e2e8f0;margin-top:14px;padding-top:14px;font-size:11px;color:#94a3b8;line-height:1.7;"">
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
    }
}

