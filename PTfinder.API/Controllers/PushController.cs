using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Services;

namespace PTfinder.API.Controllers;

[ApiController]
[Authorize]
[Route("api/push")]
public sealed class PushController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<PushController> _logger;

    public PushController(AppDbContext db, ILogger<PushController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterPushDeviceRequest? request, CancellationToken ct)
    {
        var token = request?.Token?.Trim();
        if (!IsSupportedToken(token))
            return BadRequest(new { message = "A valid Expo push token is required." });

        var (coachId, clientId) = ResolveOwner();
        if (!coachId.HasValue && !clientId.HasValue)
            return Forbid();

        await PushDeviceSchema.EnsureAsync(_db, _logger, ct);

        var device = await _db.PushDevices.FirstOrDefaultAsync(x => x.Token == token, ct);
        if (device == null)
        {
            device = new PushDevice { Token = token! };
            _db.PushDevices.Add(device);
        }

        // A token can only belong to the account currently signed in on it.
        device.CoachId = coachId;
        device.ClientId = clientId;
        device.Provider = "expo";
        device.Platform = NormalizePlatform(request?.Platform);
        device.IsActive = true;
        device.LastSeenAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { registered = true });
    }

    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister([FromBody] RegisterPushDeviceRequest? request, CancellationToken ct)
    {
        var token = request?.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token)) return NoContent();

        var (coachId, clientId) = ResolveOwner();
        var device = await _db.PushDevices.FirstOrDefaultAsync(x =>
            x.Token == token && ((coachId.HasValue && x.CoachId == coachId) || (clientId.HasValue && x.ClientId == clientId)), ct);
        if (device != null)
        {
            device.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    private (int? CoachId, int? ClientId) ResolveOwner()
    {
        var coach = ParseClaim("coachId");
        var client = ParseClaim("clientId");
        if (!client.HasValue && User.IsInRole("client")) client = ParseClaim(ClaimTypes.NameIdentifier) ?? ParseClaim(JwtRegisteredClaimNames.Sub);
        return (coach, client);
    }

    private int? ParseClaim(string type)
        => int.TryParse(User.FindFirst(type)?.Value, out var id) ? id : null;

    private static bool IsSupportedToken(string? token)
        => !string.IsNullOrWhiteSpace(token) && token.Length <= 512 &&
           (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal));

    private static string NormalizePlatform(string? platform)
        => string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase) ? "ios" : "android";
}

public sealed class RegisterPushDeviceRequest
{
    public string? Token { get; set; }
    public string? Platform { get; set; }
}
