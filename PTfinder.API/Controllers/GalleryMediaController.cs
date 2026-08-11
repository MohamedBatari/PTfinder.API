using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Services;

namespace PTfinder.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GalleryMediaController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly BlobStorageService _blobs;
    private readonly ICloudflareMediaService _cloudflare;
    private readonly ILogger<GalleryMediaController> _logger;

    public GalleryMediaController(
        AppDbContext context,
        BlobStorageService blobs,
        ICloudflareMediaService cloudflare,
        ILogger<GalleryMediaController> logger)
    {
        _context = context;
        _blobs = blobs;
        _cloudflare = cloudflare;
        _logger = logger;
    }

    // Public gallery read. Existing Azure rows and new Cloudflare rows share one response shape.
    [HttpGet("coach/{coachId:int}")]
    public async Task<ActionResult<IEnumerable<GalleryMediaDto>>> GetGalleryForCoach(
        int coachId,
        CancellationToken cancellationToken)
    {
        var items = await _context.GalleryMedia
            .AsNoTracking()
            .Where(g => g.CoachId == coachId)
            .OrderByDescending(g => g.Id)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(ToDto));
    }

    // Creates a short-lived Cloudflare upload URL. No Cloudflare secret reaches the app.
    [Authorize]
    [HttpPost("direct-upload")]
    public async Task<IActionResult> CreateDirectUpload(
        [FromBody] DirectGalleryUploadRequest request,
        CancellationToken cancellationToken)
    {
        var callerCoachId = GetCoachId();
        if (callerCoachId == null) return Unauthorized();
        if (callerCoachId.Value != request.CoachId) return Forbid();

        var mediaType = NormalizeMediaType(request.MediaType);
        if (mediaType == null)
            return BadRequest(new { message = "MediaType must be image or video." });

        if (!_cloudflare.Enabled)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code = "cloudflare_media_disabled",
                    message = "Fast media uploads are not configured yet."
                });
        }

        var maxBytes = _cloudflare.MaxBytesFor(mediaType);
        if (request.FileSize <= 0 || request.FileSize > maxBytes)
        {
            return BadRequest(new
            {
                message = $"The selected {mediaType} exceeds the {maxBytes / 1_000_000} MB limit."
            });
        }

        var coachExists = await _context.Coaches
            .AsNoTracking()
            .AnyAsync(c => c.Id == callerCoachId.Value, cancellationToken);
        if (!coachExists) return NotFound(new { message = "Coach not found." });

        try
        {
            var upload = await _cloudflare.CreateDirectUploadAsync(
                mediaType,
                callerCoachId.Value,
                request.FileSize,
                request.MaxDurationSeconds,
                cancellationToken);

            return Ok(new
            {
                upload.Provider,
                upload.ProviderId,
                upload.UploadUrl,
                upload.ExpiresAtUtc,
                MediaType = mediaType
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not create Cloudflare upload for coach {CoachId}.", callerCoachId.Value);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "The fast media service could not prepare this upload. Please try again."
            });
        }
    }

    // Confirms the phone finished uploading, then stores only a provider key in the existing Url column.
    [Authorize]
    [HttpPost("direct-upload/complete")]
    public async Task<IActionResult> CompleteDirectUpload(
        [FromBody] CompleteGalleryUploadRequest request,
        CancellationToken cancellationToken)
    {
        var callerCoachId = GetCoachId();
        if (callerCoachId == null) return Unauthorized();
        if (callerCoachId.Value != request.CoachId) return Forbid();

        var mediaType = NormalizeMediaType(request.MediaType);
        if (mediaType == null)
            return BadRequest(new { message = "MediaType must be image or video." });
        if (!_cloudflare.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Fast media uploads are not configured yet." });

        try
        {
            var check = await _cloudflare.CheckUploadAsync(
                mediaType,
                request.ProviderId,
                callerCoachId.Value,
                cancellationToken);
            if (!check.UploadReceived)
            {
                return Conflict(new
                {
                    code = "upload_not_received",
                    message = "The media upload has not reached Cloudflare yet."
                });
            }

            var storageKey = _cloudflare.BuildStorageKey(mediaType, request.ProviderId);
            var galleryMedia = await _context.GalleryMedia
                .FirstOrDefaultAsync(
                    g => g.CoachId == callerCoachId.Value && g.Url == storageKey,
                    cancellationToken);

            if (galleryMedia == null)
            {
                galleryMedia = new GalleryMedia
                {
                    Url = storageKey,
                    MediaType = mediaType,
                    CoachId = callerCoachId.Value
                };
                _context.GalleryMedia.Add(galleryMedia);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var response = ToDto(galleryMedia);
            response.ProcessingStatus = check.Status;
            return StatusCode(check.Ready ? StatusCodes.Status200OK : StatusCodes.Status202Accepted, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not complete Cloudflare upload for coach {CoachId}.", callerCoachId.Value);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "The media upload could not be confirmed. Please try again."
            });
        }
    }

    // Azure fallback. Kept for backward compatibility and disabled Cloudflare configurations.
    [Authorize]
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> UploadMedia(
        [FromForm] GalleryMediaCreateDto dto,
        CancellationToken cancellationToken)
    {
        var callerCoachId = GetCoachId();
        if (callerCoachId == null) return Unauthorized();
        if (dto == null || callerCoachId.Value != dto.CoachId) return Forbid();
        if (dto.File == null || dto.File.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        var mediaType = NormalizeMediaType(dto.MediaType);
        if (mediaType == null)
            return BadRequest(new { message = "MediaType must be image or video." });

        var extension = Path.GetExtension(dto.File.FileName);
        var blobName = $"{Guid.NewGuid()}{extension}";

        try
        {
            await using var stream = dto.File.OpenReadStream();
            await _blobs.UploadAsync(blobName, stream, dto.File.ContentType);

            var galleryMedia = new GalleryMedia
            {
                Url = blobName,
                MediaType = mediaType,
                CoachId = callerCoachId.Value
            };

            _context.GalleryMedia.Add(galleryMedia);
            await _context.SaveChangesAsync(cancellationToken);
            return Ok(ToDto(galleryMedia));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure gallery upload failed for coach {CoachId}.", callerCoachId.Value);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "The media upload failed. Please try again."
            });
        }
    }

    [Authorize]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMedia(int id, CancellationToken cancellationToken)
    {
        var callerCoachId = GetCoachId();
        if (callerCoachId == null) return Unauthorized();

        var galleryItem = await _context.GalleryMedia.FindAsync([id], cancellationToken);
        if (galleryItem == null) return NotFound(new { message = "Media not found." });
        if (galleryItem.CoachId != callerCoachId.Value) return Forbid();

        try
        {
            if (_cloudflare.TryResolve(galleryItem.Url, galleryItem.MediaType, out _))
                await _cloudflare.DeleteAsync(galleryItem.Url, cancellationToken);
            else
                await _blobs.DeleteAsync(galleryItem.Url);

            _context.GalleryMedia.Remove(galleryItem);
            await _context.SaveChangesAsync(cancellationToken);
            return Ok(new { message = "Media deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gallery media {MediaId} could not be deleted.", id);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "The media could not be deleted. Please try again."
            });
        }
    }

    private GalleryMediaDto ToDto(GalleryMedia item)
    {
        if (_cloudflare.TryResolve(item.Url, item.MediaType, out var resolved))
        {
            return new GalleryMediaDto
            {
                Id = item.Id,
                Url = resolved.MediaUrl,
                MediaUrl = resolved.MediaUrl,
                ThumbnailUrl = resolved.ThumbnailUrl,
                MediaType = item.MediaType,
                CoachId = item.CoachId,
                Provider = resolved.Provider,
                ProcessingStatus = resolved.ProcessingStatus
            };
        }

        var url = _blobs.GetReadUrl(item.Url, TimeSpan.FromMinutes(60));
        return new GalleryMediaDto
        {
            Id = item.Id,
            Url = url,
            MediaUrl = url,
            MediaType = item.MediaType,
            CoachId = item.CoachId,
            Provider = "azure-blob",
            ProcessingStatus = "ready"
        };
    }

    private int? GetCoachId()
    {
        var value =
            User.FindFirst("coachId")?.Value ??
            User.FindFirst("CoachId")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
            User.FindFirst("sub")?.Value;

        return int.TryParse(value, out var coachId) ? coachId : null;
    }

    private static string? NormalizeMediaType(string? mediaType)
    {
        if (string.Equals(mediaType?.Trim(), "image", StringComparison.OrdinalIgnoreCase)) return "image";
        if (string.Equals(mediaType?.Trim(), "video", StringComparison.OrdinalIgnoreCase)) return "video";
        return null;
    }
}

public sealed class DirectGalleryUploadRequest
{
    public int CoachId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int? MaxDurationSeconds { get; set; }
}

public sealed class CompleteGalleryUploadRequest
{
    public int CoachId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
}
