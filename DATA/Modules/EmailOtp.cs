using System;

namespace PTfinder.API.DATA.Modules
{
    public class EmailOtp
    {
        public int Id { get; set; }

        // normalized lowercase
        public string Email { get; set; } = null!;

        // SHA256(email:code) hex string
        public string CodeHash { get; set; } = null!;

        public DateTime ExpiresUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }

        // simple abuse controls
        public int Attempts { get; set; } = 0;
        public DateTime LastSentUtc { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}




