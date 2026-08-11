namespace PTfinder.API.DATA.DTO
{
    public class GalleryMediaCreateDto
    {
        public IFormFile File { get; set; } = null!;
        public string MediaType { get; set; } = string.Empty;
        public int CoachId { get; set; }
    }
}
