namespace PTfinder.API.DTO.ClientAuth
{
    public class GoogleClientLoginRequest
    {
        public string IdToken { get; set; } = null!;

        public bool TermsAccepted { get; set; }
        public string? TermsVersion { get; set; }

        public bool PrivacyAccepted { get; set; }
        public string? PrivacyVersion { get; set; }

        public string? ClientTimeZone { get; set; }
    }
}
