using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Services;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GalleryMediaController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly BlobStorageService _blobs;

        public GalleryMediaController(AppDbContext context, BlobStorageService blobs)
        {
            _context = context;
            _blobs = blobs;
        }

        // GET: api/GalleryMedia/coach/123
        [HttpGet("coach/{coachId}")]
        public async Task<ActionResult<IEnumerable<GalleryMediaDto>>> GetGalleryForCoach(int coachId)
        {
            var items = await _context.GalleryMedia
                .Where(g => g.CoachId == coachId)
                .ToListAsync();

            // We store the blob name in Url column; convert to SAS URL for output
            var result = items.Select(g => new GalleryMediaDto
            {
                Id = g.Id,
                // SAS URL valid for 60 minutes (adjust as you like)
                Url = _blobs.GetReadUrl(g.Url, TimeSpan.FromMinutes(60)),
                MediaType = g.MediaType,
                CoachId = g.CoachId
            });

            return Ok(result);
        }

        // POST: api/GalleryMedia/upload
        [HttpPost("upload")]
        [RequestSizeLimit(50_000_000)] // 50 MB; adjust for your needs
        public async Task<IActionResult> UploadMedia([FromForm] GalleryMediaCreateDto dto)
        {
            if (dto?.File == null || dto.File.Length == 0)
                return BadRequest("No file uploaded.");

            // Generate a unique blob name
            var ext = Path.GetExtension(dto.File.FileName);
            var blobName = $"{Guid.NewGuid()}{ext}";

            try
            {
                using var stream = dto.File.OpenReadStream();
                await _blobs.UploadAsync(blobName, stream, dto.File.ContentType);

                // Save metadata in SQL.
                // NOTE: we store the BLOB NAME in Url column to avoid breaking your table.
                var galleryMedia = new GalleryMedia
                {
                    Url = blobName,                 // <--- IMPORTANT: store blob name here
                    MediaType = dto.MediaType,
                    CoachId = dto.CoachId
                };

                _context.GalleryMedia.Add(galleryMedia);
                await _context.SaveChangesAsync();

                // Return a SAS URL to the client (time-limited)
                var sasUrl = _blobs.GetReadUrl(blobName, TimeSpan.FromMinutes(60));

                var responseDto = new GalleryMediaDto
                {
                    Id = galleryMedia.Id,
                    Url = sasUrl,
                    MediaType = galleryMedia.MediaType,
                    CoachId = galleryMedia.CoachId
                };

                return Ok(responseDto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // DELETE: api/GalleryMedia/123
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMedia(int id)
        {
            var galleryItem = await _context.GalleryMedia.FindAsync(id);
            if (galleryItem == null)
                return NotFound("Media not found.");

            try
            {
                // We stored the blob name in Url column
                var blobName = galleryItem.Url;
                await _blobs.DeleteAsync(blobName);

                _context.GalleryMedia.Remove(galleryItem);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Media deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
