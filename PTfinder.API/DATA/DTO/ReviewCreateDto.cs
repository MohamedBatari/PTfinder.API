namespace PTfinder.API.DATA.DTO
{
    public class ReviewCreateDto
    {
        public string StudentName { get; set; }
        public string Comment { get; set; }
        public int Rating { get; set; }
        public int CoachId { get; set; }
    }
}
