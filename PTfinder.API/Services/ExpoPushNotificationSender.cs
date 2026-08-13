using System.Net.Http.Json;
using System.Text.Json;

namespace PTfinder.API.Services;

public interface IPushNotificationSender
{
    Task SendAsync(
        IReadOnlyCollection<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        string channelId,
        CancellationToken ct = default);
}

/// <summary>
/// Sends background notifications through Expo's push gateway. The API owns
/// the delivery request, so the phone no longer needs to be open and polling.
/// </summary>
public sealed class ExpoPushNotificationSender : IPushNotificationSender
{
    private const string Endpoint = "https://exp.host/--/api/v2/push/send";
    private readonly HttpClient _http;
    private readonly ILogger<ExpoPushNotificationSender> _logger;

    public ExpoPushNotificationSender(HttpClient http, ILogger<ExpoPushNotificationSender> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task SendAsync(
        IReadOnlyCollection<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        string channelId,
        CancellationToken ct = default)
    {
        var distinct = tokens
            .Where(IsExpoToken)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length == 0) return;

        foreach (var chunk in distinct.Chunk(100))
        {
            var messages = chunk.Select(token => new
            {
                to = token,
                title,
                body,
                data,
                sound = "default",
                priority = "high",
                channelId,
                ttl = 86_400
            });

            try
            {
                using var response = await _http.PostAsJsonAsync(
                    Endpoint,
                    messages,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    ct);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("Expo push gateway returned HTTP {StatusCode}.", (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Notification persistence and SignalR delivery must remain
                // successful even if the external push gateway is unavailable.
                _logger.LogWarning(ex, "Expo push delivery failed; in-app notification remains available.");
            }
        }
    }

    private static bool IsExpoToken(string token)
        => token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) ||
           token.StartsWith("ExpoPushToken[", StringComparison.Ordinal);
}
