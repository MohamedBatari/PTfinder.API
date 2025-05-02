namespace PTfinder.API.DATA.Modules
{
    public class Review
    {
        public int Id { get; set; }
        public string StudentName { get; set; }
        public string Comment { get; set; }
        public int Rating { get; set; } // 1 to 5
        public int CoachId { get; set; }
        public Coach Coach { get; set; }
    }
}
