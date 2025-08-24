namespace PTfinder.API.DATA.DTO
{
    public class GalleryMediaCreateDto
    {
        public IFormFile File { get; set; }     
        public string MediaType { get; set; }   
        public int CoachId { get; set; }
    }
}
