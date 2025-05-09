using Microsoft.AspNetCore.Http;
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
            // ✅ 1. Find the coach by email
            var coach = _context.Coaches.SingleOrDefault(c => c.Email == request.Email);

            if (coach == null || coach.Password != request.Password)
            {
                return Unauthorized(new { message = "Invalid email or password" });
            }

            // ✅ 2. Generate JWT token
            var token = GenerateJwtToken(coach);

            // ✅ 3. Return token + coach Id
            return Ok(new
            {
                token,
                coachId = coach.Id  // 👈 Use coach.Id (NOT coach.CoachId)
            });
        }

        private string GenerateJwtToken(Coach coach)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);

            var claims = new[]
            {
            new Claim(ClaimTypes.Email, coach.Email),
            new Claim("CoachId", coach.Id.ToString())  // 👈 Again: coach.Id
        };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }

}
