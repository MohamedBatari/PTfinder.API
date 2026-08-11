using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PTfinder.API.Settings;

namespace PTfinder.API.Services;

public sealed record DirectMediaUpload(
    string Provider,
    string ProviderId,
    string UploadUrl,
    DateTimeOffset ExpiresAtUtc);

public sealed record MediaUploadCheck(
    bool UploadReceived,
    bool Ready,
    string Status);

public sealed record ResolvedMedia(
    string Provider,
    string MediaUrl,
    string? ThumbnailUrl,
    string ProcessingStatus);

public interface ICloudflareMediaService
{
    bool Enabled { get; }
    long MaxBytesFor(string mediaType);
    Task<string> UploadImageAsync(
        Stream content,
        string fileName,
        string contentType,
        int coachId,
        CancellationToken cancellationToken);
    Task<DirectMediaUpload> CreateDirectUploadAsync(
        string mediaType,
        int coachId,
        long fileSize,
        int? requestedDurationSeconds,
        CancellationToken cancellationToken);
    Task<MediaUploadCheck> CheckUploadAsync(
        string mediaType,
        string providerId,
        int coachId,
        CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
    string BuildStorageKey(string mediaType, string providerId);
    bool TryResolve(string storageKey, string mediaType, out ResolvedMedia media);
}

public sealed class CloudflareMediaService : ICloudflareMediaService
{
    private const string StreamPrefix = "cf-stream:";
    private const string ImagesPrefix = "cf-images:";

    private readonly HttpClient _httpClient;
    private readonly CloudflareMediaOptions _options;
    private readonly ILogger<CloudflareMediaService> _logger;

    public CloudflareMediaService(
        HttpClient httpClient,
        IOptions<CloudflareMediaOptions> options,
        ILogger<CloudflareMediaService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.AccountId) &&
        !string.IsNullOrWhiteSpace(_options.ApiToken) &&
        !string.IsNullOrWhiteSpace(_options.StreamCustomerCode) &&
        !string.IsNullOrWhiteSpace(_options.ImagesDeliveryHash);

    public long MaxBytesFor(string mediaType) =>
        IsVideo(mediaType) ? _options.MaxVideoBytes : _options.MaxImageBytes;

    public async Task<string> UploadImageAsync(
        Stream content,
        string fileName,
        string contentType,
        int coachId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = CreateRequest(
            HttpMethod.Post,
            $"accounts/{Uri.EscapeDataString(_options.AccountId)}/images/v1");
        using var form = new MultipartFormDataContent
        {
            { new StringContent("false"), "requireSignedURLs" },
            { new StringContent(JsonSerializer.Serialize(new { coachId, usage = "profile" })), "metadata" }
        };

        var fileContent = new StreamContent(content);
        if (MediaTypeHeaderValue.TryParse(contentType, out var parsedContentType))
            fileContent.Headers.ContentType = parsedContentType;

        form.Add(fileContent, "file", Path.GetFileName(fileName));
        request.Content = form;

        var result = await SendForResultAsync(request, cancellationToken);
        return BuildStorageKey("image", ReadRequiredString(result, "id"));
    }

    public async Task<DirectMediaUpload> CreateDirectUploadAsync(
        string mediaType,
        int coachId,
        long fileSize,
        int? requestedDurationSeconds,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        if (fileSize <= 0 || fileSize > MaxBytesFor(mediaType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileSize),
                $"The selected {NormalizeType(mediaType)} exceeds the allowed upload size.");
        }

        if (IsVideo(mediaType))
        {
            var requested = requestedDurationSeconds.GetValueOrDefault(60);
            var maxDuration = Math.Clamp(requested, 10, Math.Max(10, _options.MaxVideoDurationSeconds));
            using var request = CreateRequest(
                HttpMethod.Post,
                $"accounts/{Uri.EscapeDataString(_options.AccountId)}/stream/direct_upload");
            request.Content = JsonContent.Create(new
            {
                maxDurationSeconds = maxDuration,
                creator = $"coach:{coachId}",
                meta = new { coachId }
            });

            var result = await SendForResultAsync(request, cancellationToken);
            return new DirectMediaUpload(
                "cloudflare-stream",
                ReadRequiredString(result, "uid"),
                ReadRequiredString(result, "uploadURL"),
                DateTimeOffset.UtcNow.AddMinutes(30));
        }

        using (var request = CreateRequest(
                   HttpMethod.Post,
                   $"accounts/{Uri.EscapeDataString(_options.AccountId)}/images/v2/direct_upload"))
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent("false"), "requireSignedURLs" },
                { new StringContent(JsonSerializer.Serialize(new { coachId })), "metadata" }
            };
            request.Content = form;

            var result = await SendForResultAsync(request, cancellationToken);
            return new DirectMediaUpload(
                "cloudflare-images",
                ReadRequiredString(result, "id"),
                ReadRequiredString(result, "uploadURL"),
                DateTimeOffset.UtcNow.AddMinutes(30));
        }
    }

    public async Task<MediaUploadCheck> CheckUploadAsync(
        string mediaType,
        string providerId,
        int coachId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        EnsureProviderId(providerId);

        if (IsVideo(mediaType))
        {
            using var request = CreateRequest(
                HttpMethod.Get,
                $"accounts/{Uri.EscapeDataString(_options.AccountId)}/stream/{Uri.EscapeDataString(providerId)}");
            var result = await SendForResultAsync(request, cancellationToken);
            var creator = ReadString(result, "creator");
            if (!string.Equals(creator, $"coach:{coachId}", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("This media upload belongs to another coach.");

            var ready = result.TryGetProperty("readyToStream", out var readyValue) && readyValue.ValueKind == JsonValueKind.True;
            var status = ReadNestedString(result, "status", "state") ?? (ready ? "ready" : "processing");
            var normalizedStatus = status.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

            if (normalizedStatus is "error" or "failed")
                throw new InvalidOperationException("Cloudflare could not process this video.");

            var received = normalizedStatus is not "pendingupload" and not "pending" and not "notstarted";
            return new MediaUploadCheck(received, ready, ready ? "ready" : "processing");
        }

        using (var request = CreateRequest(
                   HttpMethod.Get,
                   $"accounts/{Uri.EscapeDataString(_options.AccountId)}/images/v1/{Uri.EscapeDataString(providerId)}"))
        {
            var result = await SendForResultAsync(request, cancellationToken);
            if (!MetadataMatchesCoach(result, coachId))
                throw new UnauthorizedAccessException("This media upload belongs to another coach.");

            var isDraft = result.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True;
            return new MediaUploadCheck(!isDraft, !isDraft, isDraft ? "uploading" : "ready");
        }
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        EnsureEnabled();

        HttpRequestMessage request;
        if (storageKey.StartsWith(StreamPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = storageKey[StreamPrefix.Length..];
            EnsureProviderId(id);
            request = CreateRequest(
                HttpMethod.Delete,
                $"accounts/{Uri.EscapeDataString(_options.AccountId)}/stream/{Uri.EscapeDataString(id)}");
        }
        else if (storageKey.StartsWith(ImagesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = storageKey[ImagesPrefix.Length..];
            EnsureProviderId(id);
            request = CreateRequest(
                HttpMethod.Delete,
                $"accounts/{Uri.EscapeDataString(_options.AccountId)}/images/v1/{Uri.EscapeDataString(id)}");
        }
        else
        {
            throw new ArgumentException("This is not a Cloudflare media key.", nameof(storageKey));
        }

        using (request)
        using (var response = await _httpClient.SendAsync(request, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cloudflare media delete failed with status {StatusCode} for provider media {StorageKey}.",
                    (int)response.StatusCode,
                    storageKey);
                response.EnsureSuccessStatusCode();
            }
        }
    }

    public string BuildStorageKey(string mediaType, string providerId)
    {
        EnsureProviderId(providerId);
        return $"{(IsVideo(mediaType) ? StreamPrefix : ImagesPrefix)}{providerId}";
    }

    public bool TryResolve(string storageKey, string mediaType, out ResolvedMedia media)
    {
        if (storageKey.StartsWith(StreamPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = storageKey[StreamPrefix.Length..];
            var root = $"https://customer-{_options.StreamCustomerCode}.cloudflarestream.com/{Uri.EscapeDataString(id)}";
            media = new ResolvedMedia(
                "cloudflare-stream",
                $"{root}/manifest/video.m3u8",
                $"{root}/thumbnails/thumbnail.jpg?time=1s&height=720",
                "ready");
            return true;
        }

        if (storageKey.StartsWith(ImagesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = storageKey[ImagesPrefix.Length..];
            var root = $"https://imagedelivery.net/{_options.ImagesDeliveryHash}/{Uri.EscapeDataString(id)}";
            media = new ResolvedMedia(
                "cloudflare-images",
                $"{root}/{Uri.EscapeDataString(_options.ImageVariant)}",
                $"{root}/{Uri.EscapeDataString(_options.ImageThumbnailVariant)}",
                "ready");
            return true;
        }

        media = default!;
        return false;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(
            method,
            $"https://api.cloudflare.com/client/v4/{relativePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        return request;
    }

    private async Task<JsonElement> SendForResultAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Cloudflare media API returned status {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException("Cloudflare could not prepare the media upload.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var success = root.TryGetProperty("success", out var successValue) && successValue.ValueKind == JsonValueKind.True;
        if (!success || !root.TryGetProperty("result", out var result))
            throw new InvalidOperationException("Cloudflare returned an invalid media response.");

        return result.Clone();
    }

    private void EnsureEnabled()
    {
        if (!Enabled)
            throw new InvalidOperationException("Cloudflare media is not configured.");
    }

    private static bool IsVideo(string mediaType) =>
        string.Equals(NormalizeType(mediaType), "video", StringComparison.Ordinal);

    private static string NormalizeType(string mediaType) =>
        string.Equals(mediaType?.Trim(), "video", StringComparison.OrdinalIgnoreCase) ? "video" : "image";

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        throw new InvalidOperationException($"Cloudflare did not return {propertyName}.");
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool MetadataMatchesCoach(JsonElement result, int coachId)
    {
        if (!result.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
            return false;
        if (!metadata.TryGetProperty("coachId", out var value)) return false;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var id) && id == coachId,
            JsonValueKind.String => int.TryParse(value.GetString(), out var id) && id == coachId,
            _ => false
        };
    }

    private static string? ReadNestedString(JsonElement element, string objectName, string propertyName)
    {
        if (element.TryGetProperty(objectName, out var nested) &&
            nested.ValueKind == JsonValueKind.Object &&
            nested.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static void EnsureProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId) ||
            providerId.Length > 180 ||
            providerId.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new ArgumentException("Invalid Cloudflare media identifier.", nameof(providerId));
        }
    }
}
