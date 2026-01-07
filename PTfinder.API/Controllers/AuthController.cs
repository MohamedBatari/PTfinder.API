using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PTfinder.API.DATA.Modules;
using PTfinder.API.DATA;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // ✅ normalize email
            var email = (request?.Email ?? string.Empty).Trim().ToLowerInvariant();
            var password = request?.Password ?? string.Empty;

            // ✅ also normalize stored email (in DB should be lower)
            var coach = _context.Coaches.SingleOrDefault(c => c.Email.ToLower() == email);

            // ⚠️ your current password is plain text compare (ok for prelaunch only)
            if (coach == null || coach.Password != password)
                return Unauthorized(new { message = "Invalid email or password" });

            if (!coach.EmailVerified)
            {
                return StatusCode(403, new
                {
                    error = "Email not verified",
                    code = "email_not_verified"
                });
            }

            var token = GenerateJwtToken(coach);

            return Ok(new
            {
                token,
                coachId = coach.Id
            });
        }

        private string GenerateJwtToken(Coach coach)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyText = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing");
            var key = Encoding.UTF8.GetBytes(keyText);

            // ✅ standard + your custom coachId
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, coach.Id.ToString()),   // ✅ BEST standard
                new Claim("coachId", coach.Id.ToString()),                   // ✅ what your BookingController reads
                new Claim(ClaimTypes.Email, coach.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Sub, coach.Id.ToString())  // optional but useful
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(8), // ✅ you can keep 1h if you want
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature
                )
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}

