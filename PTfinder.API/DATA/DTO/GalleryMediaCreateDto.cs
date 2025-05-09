namespace PTfinder.API.DATA.DTO
{
    public class GalleryMediaCreateDto
    {
        public IFormFile File { get; set; }     // The file (image/video)
        public string MediaType { get; set; }   // "image" or "video"
        public int CoachId { get; set; }
    }
}
