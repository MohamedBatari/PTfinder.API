namespace PTfinder.API.DATA.Modules
{
    public class GalleryMedia
    {
        public int Id { get; set; }
        public string Url { get; set; } 
        public string MediaType { get; set; } 

        public int CoachId { get; set; }
        public Coach Coach { get; set; }
    }
}
