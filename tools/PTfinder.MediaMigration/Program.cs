using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;

const string UserSecretsId = "22a7a58f-550b-40f4-acbc-3d375b82e478";

var options = MigrationOptions.Parse(args);
var secrets = LoadSecrets(UserSecretsId);
var connectionString = Required(secrets, "ConnectionStrings:mycon");
var storageConnection = FirstRequired(secrets, "AzureStorage:ConnectionString", "ConnectionStrings:AzureStorage");
var storageContainer = secrets.GetValueOrDefault("AzureStorage:Container") ?? "media";
var accountId = Required(secrets, "CloudflareMedia:AccountId");
var apiToken = Required(secrets, "CloudflareMedia:ApiToken");

var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
    .Options;
await using var db = new AppDbContext(dbOptions);
await db.Database.OpenConnectionAsync();

var blobContainer = new BlobServiceClient(storageConnection).GetBlobContainerClient(storageContainer);
using var http = new HttpClient
{
    BaseAddress = new Uri("https://api.cloudflare.com/client/v4/"),
    Timeout = TimeSpan.FromMinutes(10)
};
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

var candidates = await LoadCandidatesAsync(db, options);
Console.WriteLine($"Inventory: {candidates.Count} Azure media records selected.");
Console.WriteLine($"Profiles: {candidates.Count(x => x.Kind == "profile")}; gallery images: {candidates.Count(x => x.Kind == "gallery" && x.MediaType == "image")}; gallery videos: {candidates.Count(x => x.MediaType == "video")}.");

if (options.Mode == MigrationMode.DryRun)
{
    var missing = 0;
    var checkedCount = 0;
    foreach (var item in candidates)
    {
        var source = GetSourceBlob(blobContainer, item.SourceKey);
        if (!await source.ExistsAsync()) missing += 1;
        checkedCount += 1;
        if (checkedCount % 25 == 0 || checkedCount == candidates.Count)
            Console.WriteLine($"Source check: {checkedCount}/{candidates.Count}; missing: {missing}.");
    }

    if (missing > 0)
        throw new InvalidOperationException($"Dry-run failed because {missing} Azure source blobs are missing.");

    Console.WriteLine("Dry-run passed. No Cloudflare media or database records were changed.");
    return;
}

var manifestPath = Path.GetFullPath(options.ManifestPath);
var manifest = await MigrationManifest.LoadOrCreateAsync(manifestPath, candidates);

if (options.Mode == MigrationMode.Rollback)
{
    var restored = 0;
    foreach (var entry in manifest.Entries.Where(x => !string.IsNullOrWhiteSpace(x.DestinationKey)))
    {
        var changed = await RestoreDatabaseAsync(db, entry);
        if (changed)
        {
            entry.Status = "rolled-back";
            entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
            restored += 1;
            await manifest.SaveAsync(manifestPath);
        }
    }

    Console.WriteLine($"Rollback complete. Restored {restored} records. Azure and Cloudflare files were not deleted.");
    return;
}

var imageEntries = manifest.Entries
    .Where(x => x.MediaType == "image" && x.Status != "database-updated")
    .ToList();
var videoEntries = manifest.Entries
    .Where(x => x.MediaType == "video" && x.Status != "database-updated")
    .ToList();

foreach (var entry in imageEntries)
{
    try
    {
        if (string.IsNullOrWhiteSpace(entry.ProviderId))
        {
            var sourceUrl = await CreateSourceUrlAsync(blobContainer, entry.SourceKey);
            entry.ProviderId = await ImportImageAsync(http, accountId, sourceUrl, entry);
            entry.DestinationKey = $"cf-images:{entry.ProviderId}";
            entry.Status = "cloudflare-uploaded";
            entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await manifest.SaveAsync(manifestPath);
        }

        await VerifyImageAsync(http, accountId, entry.ProviderId!);
        await UpdateDatabaseAsync(db, entry);
        entry.Status = "database-updated";
        entry.Error = null;
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await manifest.SaveAsync(manifestPath);
    }
    catch (Exception ex)
    {
        entry.Status = "failed";
        entry.Error = ex.Message;
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await manifest.SaveAsync(manifestPath);
        Console.Error.WriteLine($"Image {entry.Kind}/{entry.RecordId} failed: {ex.Message}");
    }

    var complete = manifest.Entries.Count(x => x.Status == "database-updated");
    Console.WriteLine($"Migration progress: {complete}/{manifest.Entries.Count} database records switched.");
}

foreach (var entry in videoEntries.Where(x => string.IsNullOrWhiteSpace(x.ProviderId)))
{
    try
    {
        var sourceUrl = await CreateSourceUrlAsync(blobContainer, entry.SourceKey);
        entry.ProviderId = await ImportVideoAsync(http, accountId, sourceUrl, entry);
        entry.DestinationKey = $"cf-stream:{entry.ProviderId}";
        entry.Status = "processing";
        entry.Error = null;
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await manifest.SaveAsync(manifestPath);
        Console.WriteLine($"Video {entry.Kind}/{entry.RecordId} accepted by Cloudflare Stream.");
    }
    catch (Exception ex)
    {
        entry.Status = "failed";
        entry.Error = ex.Message;
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await manifest.SaveAsync(manifestPath);
        Console.Error.WriteLine($"Video {entry.Kind}/{entry.RecordId} upload failed: {ex.Message}");
    }
}

var videoDeadline = DateTimeOffset.UtcNow.AddMinutes(options.VideoWaitMinutes);
while (manifest.Entries.Any(x => x.MediaType == "video" && x.Status is "processing" or "cloudflare-uploaded") &&
       DateTimeOffset.UtcNow < videoDeadline)
{
    foreach (var entry in manifest.Entries.Where(x => x.MediaType == "video" && x.Status is "processing" or "cloudflare-uploaded"))
    {
        try
        {
            var state = await GetVideoStateAsync(http, accountId, entry.ProviderId!);
            if (state.Failed)
            {
                entry.Status = "failed";
                entry.Error = $"Cloudflare Stream processing failed ({state.State}).";
            }
            else if (state.Ready)
            {
                entry.Status = "cloudflare-uploaded";
                await UpdateDatabaseAsync(db, entry);
                entry.Status = "database-updated";
                entry.Error = null;
            }

            entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await manifest.SaveAsync(manifestPath);
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message;
            entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await manifest.SaveAsync(manifestPath);
        }
    }

    var pending = manifest.Entries.Count(x => x.MediaType == "video" && x.Status is "processing" or "cloudflare-uploaded");
    var complete = manifest.Entries.Count(x => x.Status == "database-updated");
    Console.WriteLine($"Video processing: {pending} pending; total database records switched: {complete}/{manifest.Entries.Count}.");
    if (pending > 0) await Task.Delay(TimeSpan.FromSeconds(15));
}

var succeeded = manifest.Entries.Count(x => x.Status == "database-updated");
var failed = manifest.Entries.Count(x => x.Status == "failed");
var stillProcessing = manifest.Entries.Count(x => x.Status is "processing" or "cloudflare-uploaded");
Console.WriteLine($"Migration finished. Switched: {succeeded}; failed: {failed}; still processing: {stillProcessing}.");
Console.WriteLine($"Rollback manifest: {manifestPath}");
Console.WriteLine("Azure originals were not deleted.");
if (failed > 0 || stillProcessing > 0) Environment.ExitCode = 2;

static async Task<List<MigrationCandidate>> LoadCandidatesAsync(AppDbContext db, MigrationOptions options)
{
    var candidates = new List<MigrationCandidate>();

    if (options.Kind is "all" or "profile")
    {
        candidates.AddRange(await db.Coaches
            .AsNoTracking()
            .Where(x => x.ProfileImage != null && x.ProfileImage != "" && !x.ProfileImage.StartsWith("cf-images:"))
            .OrderBy(x => x.Id)
            .Select(x => new MigrationCandidate("profile", x.Id, x.Id, "image", x.ProfileImage))
            .ToListAsync());
    }

    if (options.Kind is "all" or "gallery")
    {
        candidates.AddRange(await db.GalleryMedia
            .AsNoTracking()
            .Where(x => x.Url != null && x.Url != "" && !x.Url.StartsWith("cf-images:") && !x.Url.StartsWith("cf-stream:"))
            .OrderBy(x => x.Id)
            .Select(x => new MigrationCandidate(
                "gallery",
                x.Id,
                x.CoachId,
                x.MediaType != null && x.MediaType.ToLower().Contains("video") ? "video" : "image",
                x.Url))
            .ToListAsync());
    }

    if (options.MediaType != "all")
        candidates = candidates.Where(x => x.MediaType == options.MediaType).ToList();
    if (options.MaxItems > 0)
        candidates = candidates.Take(options.MaxItems).ToList();

    return candidates;
}

static BlobClient GetSourceBlob(BlobContainerClient container, string sourceKey)
{
    if (Uri.TryCreate(sourceKey, UriKind.Absolute, out var uri))
    {
        var containerPrefix = container.Uri.AbsoluteUri.TrimEnd('/') + "/";
        if (uri.AbsoluteUri.StartsWith(containerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var blobName = Uri.UnescapeDataString(uri.AbsolutePath[(container.Uri.AbsolutePath.TrimEnd('/').Length + 1)..]);
            return container.GetBlobClient(blobName);
        }

        return new BlobClient(uri);
    }

    return container.GetBlobClient(sourceKey);
}

static async Task<Uri> CreateSourceUrlAsync(BlobContainerClient container, string sourceKey)
{
    var blob = GetSourceBlob(container, sourceKey);
    if (!await blob.ExistsAsync())
        throw new FileNotFoundException("The Azure source blob does not exist.", sourceKey);

    if (blob.CanGenerateSasUri)
    {
        return blob.GenerateSasUri(
            BlobSasPermissions.Read,
            DateTimeOffset.UtcNow.AddHours(4));
    }

    return blob.Uri;
}

static async Task<string> ImportImageAsync(
    HttpClient http,
    string accountId,
    Uri sourceUrl,
    MigrationEntry entry)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"accounts/{Uri.EscapeDataString(accountId)}/images/v1");
    using var form = new MultipartFormDataContent
    {
        { new StringContent(sourceUrl.ToString()), "url" },
        { new StringContent("false"), "requireSignedURLs" },
        { new StringContent(JsonSerializer.Serialize(new { coachId = entry.CoachId, source = "azure-migration", kind = entry.Kind, recordId = entry.RecordId })), "metadata" }
    };
    request.Content = form;
    var result = await SendForResultAsync(http, request);
    return RequiredString(result, "id");
}

static async Task VerifyImageAsync(HttpClient http, string accountId, string providerId)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"accounts/{Uri.EscapeDataString(accountId)}/images/v1/{Uri.EscapeDataString(providerId)}");
    var result = await SendForResultAsync(http, request);
    if (result.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
        throw new InvalidOperationException("Cloudflare image is still a draft.");
}

static async Task<string> ImportVideoAsync(
    HttpClient http,
    string accountId,
    Uri sourceUrl,
    MigrationEntry entry)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"accounts/{Uri.EscapeDataString(accountId)}/stream/copy");
    request.Content = JsonContent.Create(new
    {
        input = sourceUrl.ToString(),
        creator = $"coach:{entry.CoachId}",
        name = $"ptfindernow-{entry.Kind}-{entry.RecordId}",
        requireSignedURLs = false,
        meta = new Dictionary<string, string>
        {
            ["coachId"] = entry.CoachId.ToString(),
            ["source"] = "azure-migration",
            ["kind"] = entry.Kind,
            ["recordId"] = entry.RecordId.ToString()
        }
    });
    var result = await SendForResultAsync(http, request);
    return RequiredString(result, "uid");
}

static async Task<VideoState> GetVideoStateAsync(HttpClient http, string accountId, string providerId)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"accounts/{Uri.EscapeDataString(accountId)}/stream/{Uri.EscapeDataString(providerId)}");
    var result = await SendForResultAsync(http, request);
    var ready = result.TryGetProperty("readyToStream", out var readyValue) && readyValue.ValueKind == JsonValueKind.True;
    var state = result.TryGetProperty("status", out var status) &&
                status.TryGetProperty("state", out var stateValue)
        ? stateValue.GetString() ?? "unknown"
        : "unknown";
    var failed = state.Equals("error", StringComparison.OrdinalIgnoreCase) ||
                 state.Equals("failed", StringComparison.OrdinalIgnoreCase);
    return new VideoState(ready, failed, state);
}

static async Task<JsonElement> SendForResultAsync(HttpClient http, HttpRequestMessage request)
{
    using var response = await http.SendAsync(request);
    var payload = await response.Content.ReadAsStringAsync();
    using var document = JsonDocument.Parse(payload);
    var root = document.RootElement;
    var success = root.TryGetProperty("success", out var value) && value.ValueKind == JsonValueKind.True;
    if (!response.IsSuccessStatusCode || !success || !root.TryGetProperty("result", out var result))
    {
        var message = root.TryGetProperty("errors", out var errors) ? errors.ToString() : response.ReasonPhrase;
        throw new InvalidOperationException($"Cloudflare request failed ({(int)response.StatusCode}): {message}");
    }

    return result.Clone();
}

static string RequiredString(JsonElement result, string property)
{
    if (result.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()))
        return value.GetString()!;

    throw new InvalidOperationException($"Cloudflare did not return {property}.");
}

static async Task UpdateDatabaseAsync(AppDbContext db, MigrationEntry entry)
{
    if (string.IsNullOrWhiteSpace(entry.DestinationKey))
        throw new InvalidOperationException("The Cloudflare destination key is missing.");

    var changed = entry.Kind == "profile"
        ? await db.Coaches
            .Where(x => x.Id == entry.RecordId && x.ProfileImage == entry.SourceKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProfileImage, entry.DestinationKey))
        : await db.GalleryMedia
            .Where(x => x.Id == entry.RecordId && x.Url == entry.SourceKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Url, entry.DestinationKey));

    if (changed == 1) return;

    var alreadyUpdated = entry.Kind == "profile"
        ? await db.Coaches.AnyAsync(x => x.Id == entry.RecordId && x.ProfileImage == entry.DestinationKey)
        : await db.GalleryMedia.AnyAsync(x => x.Id == entry.RecordId && x.Url == entry.DestinationKey);
    if (!alreadyUpdated)
        throw new InvalidOperationException("The database record changed after inventory; it was not overwritten.");
}

static async Task<bool> RestoreDatabaseAsync(AppDbContext db, MigrationEntry entry)
{
    if (string.IsNullOrWhiteSpace(entry.DestinationKey)) return false;

    var changed = entry.Kind == "profile"
        ? await db.Coaches
            .Where(x => x.Id == entry.RecordId && x.ProfileImage == entry.DestinationKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProfileImage, entry.SourceKey))
        : await db.GalleryMedia
            .Where(x => x.Id == entry.RecordId && x.Url == entry.DestinationKey)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Url, entry.SourceKey));
    return changed == 1;
}

static Dictionary<string, string> LoadSecrets(string userSecretsId)
{
    var path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "UserSecrets",
        userSecretsId,
        "secrets.json");
    if (!File.Exists(path)) throw new FileNotFoundException("The backend user-secrets file was not found.");
    return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
           ?? throw new InvalidOperationException("The backend user-secrets file is invalid.");
}

static string Required(Dictionary<string, string> values, string key) =>
    values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Required local secret is missing: {key}");

static string FirstRequired(Dictionary<string, string> values, params string[] keys)
{
    foreach (var key in keys)
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
    throw new InvalidOperationException($"Required local secret is missing: {string.Join(" or ", keys)}");
}

internal enum MigrationMode { DryRun, Apply, Rollback }

internal sealed record MigrationOptions(
    MigrationMode Mode,
    string Kind,
    string MediaType,
    int MaxItems,
    int VideoWaitMinutes,
    string ManifestPath)
{
    public static MigrationOptions Parse(string[] args)
    {
        var mode = args.Contains("--apply") ? MigrationMode.Apply :
            args.Contains("--rollback") ? MigrationMode.Rollback : MigrationMode.DryRun;
        var kind = ValueAfter(args, "--kind")?.ToLowerInvariant() ?? "all";
        var mediaType = ValueAfter(args, "--media")?.ToLowerInvariant() ?? "all";
        var max = int.TryParse(ValueAfter(args, "--max"), out var parsedMax) ? parsedMax : 0;
        var wait = int.TryParse(ValueAfter(args, "--video-wait-minutes"), out var parsedWait) ? parsedWait : 45;
        var manifest = ValueAfter(args, "--manifest") ?? Path.Combine("migration-backups", "cloudflare-media-manifest.json");

        if (kind is not ("all" or "profile" or "gallery")) throw new ArgumentException("--kind must be all, profile, or gallery.");
        if (mediaType is not ("all" or "image" or "video")) throw new ArgumentException("--media must be all, image, or video.");
        if (mode == MigrationMode.Rollback && !File.Exists(manifest)) throw new FileNotFoundException("Rollback manifest not found.", manifest);
        return new MigrationOptions(mode, kind, mediaType, max, Math.Clamp(wait, 5, 180), manifest);
    }

    private static string? ValueAfter(string[] values, string name)
    {
        var index = Array.IndexOf(values, name);
        return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
    }
}

internal sealed record MigrationCandidate(string Kind, int RecordId, int CoachId, string MediaType, string SourceKey);
internal sealed record VideoState(bool Ready, bool Failed, string State);

internal sealed class MigrationEntry
{
    public string Kind { get; set; } = "";
    public int RecordId { get; set; }
    public int CoachId { get; set; }
    public string MediaType { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string? ProviderId { get; set; }
    public string? DestinationKey { get; set; }
    public string Status { get; set; } = "pending";
    public string? Error { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class MigrationManifest
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<MigrationEntry> Entries { get; set; } = [];

    public static async Task<MigrationManifest> LoadOrCreateAsync(
        string path,
        IReadOnlyCollection<MigrationCandidate> candidates)
    {
        MigrationManifest manifest;
        if (File.Exists(path))
        {
            manifest = JsonSerializer.Deserialize<MigrationManifest>(await File.ReadAllTextAsync(path))
                       ?? throw new InvalidOperationException("The migration manifest is invalid.");
        }
        else
        {
            manifest = new MigrationManifest();
        }

        foreach (var candidate in candidates)
        {
            if (manifest.Entries.Any(x => x.Kind == candidate.Kind && x.RecordId == candidate.RecordId)) continue;
            manifest.Entries.Add(new MigrationEntry
            {
                Kind = candidate.Kind,
                RecordId = candidate.RecordId,
                CoachId = candidate.CoachId,
                MediaType = candidate.MediaType,
                SourceKey = candidate.SourceKey
            });
        }

        await manifest.SaveAsync(path);
        return manifest;
    }

    public async Task SaveAsync(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(temporary, json);
        File.Move(temporary, path, true);
    }
}
