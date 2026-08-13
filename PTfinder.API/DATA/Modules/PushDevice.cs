namespace PTfinder.API.DATA.Modules;

/// <summary>
/// A device push token owned by one authenticated coach or client.
/// Tokens are intentionally separate from notification history so installing
/// the app on another phone never changes or deletes existing notifications.
/// </summary>
public sealed class PushDevice
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Provider { get; set; } = "expo";
    public string Platform { get; set; } = "android";
    public int? CoachId { get; set; }
    public int? ClientId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}
