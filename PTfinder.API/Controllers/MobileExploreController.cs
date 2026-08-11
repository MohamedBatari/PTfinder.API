using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Services;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MobileExploreController : ControllerBase
    {
        private const int MaxTake = 160;

        private readonly AppDbContext _context;
        private readonly BlobStorageService _blobs;
        private readonly ICloudflareMediaService _cloudflare;

        public MobileExploreController(
            AppDbContext context,
            BlobStorageService blobs,
            ICloudflareMediaService cloudflare)
        {
            _context = context;
            _blobs = blobs;
            _cloudflare = cloudflare;
        }

        [HttpGet("feed")]
        public async Task<ActionResult<MobileExploreFeedResponse>> GetFeed(
            [FromQuery] string? filter = "all",
            [FromQuery] string? search = null,
            [FromQuery] DateTime? date = null,
            [FromQuery] int take = 120,
            CancellationToken ct = default)
        {
            var normalizedFilter = NormalizeFilter(filter);
            var today = (date ?? DateTime.UtcNow).Date;
            var safeTake = Math.Clamp(take, 12, MaxTake);

            var query = _context.Coaches
                .AsNoTracking()
                .Include(c => c.Category)
                .Include(c => c.Speciality)
                .Include(c => c.Country)
                .Include(c => c.City)
                .Include(c => c.Area)
                .Include(c => c.GalleryMedia)
                .Include(c => c.Availabilities)
                .Where(c =>
                    c.IsActive &&
                    c.EmailVerified);

            if (normalizedFilter is "female" or "male")
            {
                query = query.Where(c =>
                    c.Gender != null &&
                    c.Gender.ToLower() == (normalizedFilter == "female" ? "female" : "male"));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().Contains(needle));
            }

            if (normalizedFilter == "featured")
            {
                var nowUtc = DateTime.UtcNow;
                query = query.Where(c =>
                    (c.PartnerId != null || c.SubscriptionTier == SubscriptionTier.Standard) &&
                    c.SubscriptionTier > SubscriptionTier.None &&
                    (
                        (c.SubscriptionExpiresAtUtc.HasValue && c.SubscriptionExpiresAtUtc > nowUtc) ||
                        (c.CurrentPeriodEndUtc.HasValue && c.CurrentPeriodEndUtc > nowUtc)
                    ));
            }

            if (normalizedFilter == "availableToday")
            {
                query = query.Where(c => c.Availabilities.Any(a => a.AvailableDate.Date == today));
            }

            var coaches = await query.ToListAsync(ct);
            var coachIds = coaches.Select(c => c.Id).ToArray();

            var reviewStats = await _context.Reviews
                .AsNoTracking()
                .Where(r => coachIds.Contains(r.CoachId))
                .GroupBy(r => r.CoachId)
                .Select(g => new
                {
                    CoachId = g.Key,
                    Count = g.Count(),
                    Average = g.Average(r => (double)r.Rating)
                })
                .ToDictionaryAsync(x => x.CoachId, x => x, ct);

            var items = coaches
                .Select(coach =>
                {
                    reviewStats.TryGetValue(coach.Id, out var stats);
                    return CreateFeedItem(coach, stats?.Count ?? 0, stats?.Average ?? 0, today, normalizedFilter);
                })
                .Where(item => item != null)
                .Cast<MobileExploreFeedItem>()
                .ToList();

            if (normalizedFilter == "video")
            {
                items = items
                    .Where(item => item.MediaType == "video")
                    .ToList();
            }

            items = OrderItems(items, normalizedFilter, today)
                .Take(safeTake)
                .Select((item, index) => item with { FeedOrder = index })
                .ToList();

            return Ok(new MobileExploreFeedResponse(
                GeneratedAtUtc: DateTime.UtcNow,
                Date: today.ToString("yyyy-MM-dd"),
                Filter: normalizedFilter,
                TotalCoaches: coaches.Count,
                Items: items));
        }

        private MobileExploreFeedItem? CreateFeedItem(
            Coach coach,
            int reviewCount,
            double averageRating,
            DateTime today,
            string filter)
        {
            var media = SelectBestMedia(coach.GalleryMedia, filter);
            var profileImage = ToReadUrl(coach.ProfileImage);
            var mediaUrl = profileImage;
            var thumbnailUrl = profileImage;

            if (media != null)
            {
                if (_cloudflare.TryResolve(media.Url, media.MediaType, out var resolved))
                {
                    mediaUrl = resolved.MediaUrl;
                    thumbnailUrl = resolved.ThumbnailUrl ?? profileImage ?? resolved.MediaUrl;
                }
                else
                {
                    mediaUrl = ToReadUrl(media.Url);
                    thumbnailUrl = profileImage ?? mediaUrl;
                }
            }

            if (string.IsNullOrWhiteSpace(mediaUrl))
                return null;

            var mediaType = media != null && IsVideo(media.Url, media.MediaType) ? "video" : "image";
            if (filter == "video" && mediaType != "video")
                return null;

            var validGalleryCount = coach.GalleryMedia?
                .Count(item => HasSupportedMedia(item.Url, item.MediaType)) ?? 0;

            var availableToday = coach.Availabilities?
                .Any(a => a.AvailableDate.Date == today) ?? false;

            var nowUtc = DateTime.UtcNow;
            var hasActiveSubscription =
                coach.SubscriptionTier > SubscriptionTier.None &&
                (
                    (coach.SubscriptionExpiresAtUtc.HasValue && coach.SubscriptionExpiresAtUtc > nowUtc) ||
                    (coach.CurrentPeriodEndUtc.HasValue && coach.CurrentPeriodEndUtc > nowUtc)
                );
            var isFeatured = hasActiveSubscription &&
                (coach.PartnerId != null || coach.SubscriptionTier == SubscriptionTier.Standard);

            var caption = string.Join(" • ", new[]
            {
                coach.Speciality?.Name,
                coach.City?.Name,
                coach.Area?.Name
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

            if (string.IsNullOrWhiteSpace(caption))
                caption = "Personal trainer";

            var coachDto = new MobileExploreCoachDto(
                Id: coach.Id,
                FullName: coach.FullName,
                Gender: coach.Gender,
                Price: coach.Price,
                // The mobile feed never renders the long coach bio. Excluding it
                // keeps the first Explore response small and fast on cellular data.
                Description: null,
                ProfileImage: profileImage,
                CategoryName: coach.Category?.Name,
                SpecialtyName: coach.Speciality?.Name,
                CountryName: coach.Country?.Name,
                CityName: coach.City?.Name,
                AreaName: coach.Area?.Name,
                AvgRating: Math.Round(averageRating, 2),
                NumReviews: reviewCount,
                SubscriptionTier: (int)coach.SubscriptionTier,
                SubscriptionStatus: (int)coach.SubscriptionStatus,
                SubscriptionExpiresAtUtc: coach.SubscriptionExpiresAtUtc,
                CurrentPeriodEndUtc: coach.CurrentPeriodEndUtc,
                IsVerified: coach.EmailVerified,
                IsFeatured: isFeatured,
                AvailableToday: availableToday,
                CreatedAtUtc: coach.CreatedAtUtc);

            var mediaId = media?.Id.ToString() ?? $"profile-{coach.Id}";
            var score = ScoreItem(coach, mediaType, reviewCount, averageRating, today, isFeatured);

            return new MobileExploreFeedItem(
                Id: $"{coach.Id}-{mediaId}-server",
                MediaId: mediaId,
                Coach: coachDto,
                CoachId: coach.Id,
                FeedOrder: 0,
                Source: media == null ? "profile" : "gallery",
                MediaUrl: mediaUrl,
                ThumbUrl: mediaType == "video" ? thumbnailUrl : mediaUrl,
                MediaType: mediaType,
                Caption: caption,
                MediaCount: Math.Max(1, validGalleryCount),
                SpotlightScore: score,
                RankScore: score,
                Loop: 0,
                CreatedAtLabel: media == null ? "Featured now" : "Latest from gallery",
                AvailableToday: availableToday,
                IsFeatured: isFeatured);
        }

        private GalleryMedia? SelectBestMedia(IEnumerable<GalleryMedia>? gallery, string filter)
        {
            var candidates = (gallery ?? Enumerable.Empty<GalleryMedia>())
                .Where(item => HasSupportedMedia(item.Url, item.MediaType))
                .Select((item, index) => new
                {
                    Item = item,
                    Score = MediaScore(item, index)
                });

            if (filter == "video")
                candidates = candidates.Where(x => IsVideo(x.Item.Url, x.Item.MediaType));

            return candidates
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)
                .FirstOrDefault();
        }

        private string? ToReadUrl(string? blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                return null;

            if (_cloudflare.TryResolve(blobName, "image", out var cloudflareMedia))
                return cloudflareMedia.MediaUrl;

            if (blobName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                blobName.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                blobName.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                return blobName;
            }

            return _blobs.GetReadUrl(blobName, TimeSpan.FromMinutes(60));
        }

        private static IEnumerable<MobileExploreFeedItem> OrderItems(
            List<MobileExploreFeedItem> items,
            string filter,
            DateTime today)
        {
            if (filter == "topRated")
            {
                return items.OrderByDescending(item => item.Coach.AvgRating)
                    .ThenByDescending(item => item.Coach.NumReviews)
                    .ThenByDescending(item => item.RankScore);
            }

            if (filter == "featured")
            {
                return items.OrderByDescending(item => item.Coach.SubscriptionTier)
                    .ThenByDescending(item => item.RankScore);
            }

            var ranked = items
                .OrderByDescending(item => item.RankScore + FairRotation(item.CoachId, today))
                .ToList();

            var buckets = new Dictionary<string, Queue<MobileExploreFeedItem>>
            {
                ["female"] = new(ranked.Where(item => GenderBucket(item.Coach.Gender) == "female")),
                ["male"] = new(ranked.Where(item => GenderBucket(item.Coach.Gender) == "male")),
                ["unknown"] = new(ranked.Where(item => GenderBucket(item.Coach.Gender) == "unknown"))
            };

            var pattern = new[] { "female", "female", "female", "male", "female", "male", "unknown" };
            var ordered = new List<MobileExploreFeedItem>(ranked.Count);
            var patternIndex = 0;

            while (buckets.Values.Any(queue => queue.Count > 0))
            {
                var preferred = pattern[patternIndex % pattern.Length];
                var next = DequeuePreferred(buckets, preferred);
                if (next != null)
                    ordered.Add(next);

                patternIndex += 1;
            }

            return ordered;
        }

        private static MobileExploreFeedItem? DequeuePreferred(
            Dictionary<string, Queue<MobileExploreFeedItem>> buckets,
            string preferred)
        {
            if (buckets[preferred].Count > 0)
                return buckets[preferred].Dequeue();

            return buckets
                .OrderByDescending(pair => GenderPriority(pair.Key))
                .FirstOrDefault(pair => pair.Value.Count > 0)
                .Value?
                .Dequeue();
        }

        private static double ScoreItem(
            Coach coach,
            string mediaType,
            int reviewCount,
            double averageRating,
            DateTime today,
            bool isFeatured)
        {
            var mediaBoost = mediaType == "video" ? 32 : 18;
            var ratingBoost = averageRating > 0 ? averageRating * 10 : 4;
            var reviewBoost = Math.Min(28, Math.Log10(reviewCount + 1) * 18);
            var featuredBoost = isFeatured ? 10 : 0;
            var verifiedBoost = coach.EmailVerified ? 7 : 0;
            var genderBoost = GenderPriority(GenderBucket(coach.Gender));
            var freshnessBoost = Math.Max(0, 10 - (today - coach.CreatedAtUtc.Date).TotalDays / 15);

            return mediaBoost + ratingBoost + reviewBoost + featuredBoost + verifiedBoost + genderBoost + freshnessBoost;
        }

        private static int MediaScore(GalleryMedia media, int index)
        {
            var score = IsVideo(media.Url, media.MediaType) ? 40 : 26;
            if (IsMp4(media.Url)) score += 18;
            if (IsMov(media.Url)) score -= 8;
            return score - index;
        }

        private static bool HasSupportedMedia(string? url, string? mediaType)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (url.StartsWith("cf-stream:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("cf-images:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsVideo(url, mediaType) || IsImage(url);
        }

        private static bool IsVideo(string? url, string? mediaType)
        {
            if (url?.StartsWith("cf-stream:", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            var rawType = mediaType?.Trim().ToLowerInvariant() ?? "";
            if (rawType.Contains("video"))
                return true;

            var path = StripQuery(url);
            return path.EndsWith(".mp4") ||
                   path.EndsWith(".mov") ||
                   path.EndsWith(".webm") ||
                   path.EndsWith(".m4v");
        }

        private static bool IsImage(string? url)
        {
            if (url?.StartsWith("cf-images:", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            var path = StripQuery(url);
            return path.EndsWith(".jpg") ||
                   path.EndsWith(".jpeg") ||
                   path.EndsWith(".png") ||
                   path.EndsWith(".webp") ||
                   path.EndsWith(".gif") ||
                   path.EndsWith(".heic") ||
                   path.EndsWith(".bmp");
        }

        private static bool IsMp4(string? url) => StripQuery(url).EndsWith(".mp4");
        private static bool IsMov(string? url) => StripQuery(url).EndsWith(".mov");

        private static string StripQuery(string? value)
        {
            var raw = value?.Trim().ToLowerInvariant() ?? "";
            var queryIndex = raw.IndexOf('?');
            return queryIndex >= 0 ? raw[..queryIndex] : raw;
        }

        private static string NormalizeFilter(string? filter)
        {
            var normalized = filter?.Trim().ToLowerInvariant() ?? "all";
            return normalized switch
            {
                "saved" => "all",
                "today" => "availableToday",
                "availabletoday" => "availableToday",
                "featured" => "featured",
                "toprated" => "topRated",
                "video" => "video",
                "videos" => "video",
                "female" => "female",
                "women" => "female",
                "male" => "male",
                "men" => "male",
                _ => "all"
            };
        }

        private static string GenderBucket(string? gender)
        {
            var raw = gender?.Trim().ToLowerInvariant() ?? "";
            if (raw is "female" or "f" || raw.Contains("woman") || raw.Contains("women") || raw.Contains("lady"))
                return "female";
            if (raw is "male" or "m" || raw.Contains("man") || raw.Contains("men"))
                return "male";
            return "unknown";
        }

        private static int GenderPriority(string gender) => gender switch
        {
            "female" => 34,
            "male" => 12,
            _ => 8
        };

        private static double FairRotation(int coachId, DateTime day)
        {
            var input = $"mobile-feed-{coachId}-{day:yyyy-MM-dd}";
            uint hash = 2166136261;

            foreach (var ch in input)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return (hash / (double)uint.MaxValue) * 18;
        }
    }

    public sealed record MobileExploreFeedResponse(
        DateTime GeneratedAtUtc,
        string Date,
        string Filter,
        int TotalCoaches,
        IReadOnlyList<MobileExploreFeedItem> Items);

    public sealed record MobileExploreFeedItem(
        string Id,
        string MediaId,
        MobileExploreCoachDto Coach,
        int CoachId,
        int FeedOrder,
        string Source,
        string MediaUrl,
        string? ThumbUrl,
        string MediaType,
        string Caption,
        int MediaCount,
        double SpotlightScore,
        double RankScore,
        int Loop,
        string CreatedAtLabel,
        bool AvailableToday,
        bool IsFeatured);

    public sealed record MobileExploreCoachDto(
        int Id,
        string? FullName,
        string? Gender,
        decimal Price,
        string? Description,
        string? ProfileImage,
        string? CategoryName,
        string? SpecialtyName,
        string? CountryName,
        string? CityName,
        string? AreaName,
        double AvgRating,
        int NumReviews,
        int SubscriptionTier,
        int SubscriptionStatus,
        DateTime? SubscriptionExpiresAtUtc,
        DateTime? CurrentPeriodEndUtc,
        bool IsVerified,
        bool IsFeatured,
        bool AvailableToday,
        DateTime CreatedAtUtc);
}
