using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA;
using PTfinder.API.DATA.DTO;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GalleryMediaController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Supabase.Client _supabase;

        public GalleryMediaController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;

            var supabaseUrl = configuration["Supabase:Url"];
            var supabaseKey = configuration["Supabase:Key"];

            _supabase = new Supabase.Client(supabaseUrl, supabaseKey);
            _supabase.InitializeAsync().Wait();
        }

        // ✅ Get gallery by coach
        [HttpGet("coach/{coachId}")]
        public async Task<ActionResult<IEnumerable<GalleryMediaDto>>> GetGalleryForCoach(int coachId)
        {
            var gallery = await _context.GalleryMedia
                .Where(g => g.CoachId == coachId)
                .Select(g => new GalleryMediaDto
                {
                    Id = g.Id,
                    Url = g.Url,
                    MediaType = g.MediaType,
                    CoachId = g.CoachId
                })
                .ToListAsync();

            return Ok(gallery);
        }

        // ✅ Upload new media
        [HttpPost("upload")]
        public async Task<IActionResult> UploadMedia([FromForm] GalleryMediaCreateDto dto)
        {
            if (dto.File == null || dto.File.Length == 0)
                return BadRequest("No file uploaded.");

            var bucketName = "coach-gallery"; // ✅ Your bucket name

            var storage = _supabase.Storage;
            var bucket = storage.From(bucketName);

            var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(dto.File.FileName)}";

            try
            {
                using (var stream = dto.File.OpenReadStream())
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await stream.CopyToAsync(memoryStream);
                        var fileBytes = memoryStream.ToArray();

                        await bucket.Upload(fileBytes, uniqueFileName, new Supabase.Storage.FileOptions
                        {
                            ContentType = dto.File.ContentType,
                            Upsert = true
                        });
                    }
                }


                // Get the public URL
                var publicUrl = bucket.GetPublicUrl(uniqueFileName);

                var galleryMedia = new GalleryMedia
                {
                    Url = publicUrl,
                    MediaType = dto.MediaType,
                    CoachId = dto.CoachId
                };

                _context.GalleryMedia.Add(galleryMedia);
                await _context.SaveChangesAsync();

                var responseDto = new GalleryMediaDto
                {
                    Id = galleryMedia.Id,
                    Url = galleryMedia.Url,
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


        // ✅ DELETE media by id
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMedia(int id)
        {
            var galleryItem = await _context.GalleryMedia.FindAsync(id);
            if (galleryItem == null)
                return NotFound("Media not found.");

            var bucketName = "coach-gallery"; // same bucket name
            var storage = _supabase.Storage;
            var bucket = storage.From(bucketName);

            // Get the file name from the URL (Supabase public URLs contain the file path after the bucket)
            var filePath = new Uri(galleryItem.Url).Segments.Last();

            try
            {
                // ❌ Delete from Supabase
                await bucket.Remove(new List<string> { filePath });

                // 🗑️ Delete from database
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
