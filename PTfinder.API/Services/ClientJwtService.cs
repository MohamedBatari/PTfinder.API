using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PTfinder.API.Models;

namespace PTfinder.API.Services
{
    public class ClientJwtService : IClientJwtService
    {
        private readonly IConfiguration _config;

        public ClientJwtService(IConfiguration config)
        {
            _config = config;
        }

        public string GenerateToken(Client client)
        {
            var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing.");
            var issuer = _config["Jwt:Issuer"];
            var audience = _config["Jwt:Audience"];

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, client.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, client.Email),
                new Claim("clientId", client.Id.ToString()),
                new Claim("role", "client"),
                new Claim("fullName", client.FullName)
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
