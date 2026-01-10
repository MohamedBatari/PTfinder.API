// Settings/SmtpSettings.cs
namespace PTfinder.API.Settings
{
    public sealed class SmtpSettings
    {
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public string User { get; set; } = "noreply@gmail.com";
        public string Pass { get; set; } = ""; // Gmail App Password
        public string ReplyTo { get; set; } = "info@ptfindernow.com";

        // ✅ One sender only
        public string From { get; set; } = "PTfinderNow <noreply@gmail.com>";
        public string? Bcc { get; set; } // e.g. "noreply@ptfindernow.com"

    }
}

