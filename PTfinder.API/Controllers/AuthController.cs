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
            // basic null/trim normalization
            var email = (request?.Email ?? string.Empty).Trim();
            var password = request?.Password ?? string.Empty;

            var coach = _context.Coaches.SingleOrDefault(c => c.Email == email);

            // Invalid email or password (your current implementation uses plain-text compare)
            if (coach == null || coach.Password != password)
            {
                return Unauthorized(new { message = "Invalid email or password" });
            }

            // 🔒 Enforce email verification before issuing a JWT
            // Make sure your Coach entity has: bool EmailVerified { get; set; }
            if (!coach.EmailVerified)
            {
                // Frontend should show a message and call POST /api/auth/request-verification { email }
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
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);

            var claims = new[]
            {
                new Claim(ClaimTypes.Email, coach.Email),
                new Claim("CoachId", coach.Id.ToString())
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
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
