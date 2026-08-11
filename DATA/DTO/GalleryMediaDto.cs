namespace PTfinder.API.DATA.DTO
{
    public class GalleryMediaDto
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string? MediaUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public int CoachId { get; set; }
        public string Provider { get; set; } = "azure-blob";
        public string ProcessingStatus { get; set; } = "ready";
    }
}
