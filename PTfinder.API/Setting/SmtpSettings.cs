// Settings/SmtpSettings.cs
namespace PTfinder.API.Settings
{
    public sealed class SmtpSettings
    {
        public string Host { get; set; } = "mail.smtp2go.com";
        public int Port { get; set; } = 2525; // 587 also works
        public string User { get; set; } = "";
        public string Pass { get; set; } = "";
        public string ReplyTo { get; set; } = "info@ptfindernow.com";

        // Keep property name the same for callers:
        public AddressSet FromAddresses { get; set; } = new();

        public string? ConfigSet { get; set; } = null;

        // Rename the nested type to avoid “ambiguity” style errors
        public sealed class AddressSet
        {
            public string Default { get; set; } = "noreply@ptfindernow.com";
            public string Verification { get; set; } = "verification@ptfindernow.com";
            public string Booking { get; set; } = "confirmation@ptfindernow.com";
            public string Welcome { get; set; } = "welcome@ptfindernow.com";
            public string Info { get; set; } = "info@ptfindernow.com";
        }
    }
}

