namespace PTfinder.API.DATA.Modules
{
    public class GalleryMedia
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;

        public int CoachId { get; set; }
        public Coach Coach { get; set; } = null!;
    }
}
