// DATA/Modules/EmailVerification.cs
using System.ComponentModel.DataAnnotations;

namespace PTfinder.API.DATA.Modules
{
    public class EmailVerification
    {
        public int Id { get; set; }

        [Required, MaxLength(256)]
        public string Email { get; set; } = null!; // normalized lowercased

        [Required, MaxLength(200)]
        public string Token { get; set; } = null!; // url-safe token

        public DateTime ExpiresUtc { get; set; }

        public DateTime? UsedAtUtc { get; set; } // null => not used yet
    }
}

