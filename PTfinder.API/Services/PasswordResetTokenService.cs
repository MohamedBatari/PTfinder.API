using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Services;

public sealed record PasswordResetTokenData(int CoachId, string PasswordFingerprint);

public interface IPasswordResetTokenService
{
    string Issue(Coach coach, int lifetimeMinutes);
    PasswordResetTokenData? Validate(string token);
    bool MatchesCurrentPassword(string tokenFingerprint, string currentPassword);
}

public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private readonly IConfiguration _configuration;

    public PasswordResetTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Issue(Coach coach, int lifetimeMinutes)
    {
        var now = DateTime.UtcNow;
        var signingKey = GetSigningKey();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, coach.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim("purpose", "password-reset"),
            new Claim("pwdv", CreatePasswordFingerprint(coach.Password, signingKey))
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(lifetimeMinutes),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(signingKey),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public PasswordResetTokenData? Validate(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(GetSigningKey()),
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ClockSkew = TimeSpan.FromMinutes(1)
                },
                out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal) ||
                principal.FindFirst("purpose")?.Value != "password-reset" ||
                !int.TryParse(
                    principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var coachId))
            {
                return null;
            }

            var passwordFingerprint = principal.FindFirst("pwdv")?.Value;
            return string.IsNullOrWhiteSpace(passwordFingerprint)
                ? null
                : new PasswordResetTokenData(coachId, passwordFingerprint);
        }
        catch (Exception exception) when (
            exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    public bool MatchesCurrentPassword(string tokenFingerprint, string currentPassword)
    {
        var expected = CreatePasswordFingerprint(currentPassword, GetSigningKey());
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(tokenFingerprint),
            Encoding.UTF8.GetBytes(expected));
    }

    private byte[] GetSigningKey()
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        return HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(jwtKey),
            Encoding.UTF8.GetBytes("PTfinderNow/password-reset/v1"));
    }

    private static string CreatePasswordFingerprint(string passwordHash, byte[] signingKey) =>
        Base64UrlEncoder.Encode(HMACSHA256.HashData(
            signingKey,
            Encoding.UTF8.GetBytes(passwordHash ?? string.Empty)));
}
