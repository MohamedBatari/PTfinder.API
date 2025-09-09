using System;

namespace PTfinder.API.DATA.Modules
{
    public class EmailOtp
    {
        public int Id { get; set; }
        public string Email { get; set; } = default!;
        public string CodeHash { get; set; } = default!;
        public DateTime ExpiresUtc { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public int Attempts { get; set; } = 0;
        public DateTime LastSentUtc { get; set; } = DateTime.UtcNow;
    }
}


