namespace PTfinder.API.DATA.Modules
{
    public class Subscription
    {
        public int Id { get; set; }
        public int CoachId { get; set; }
        public Coach Coach { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
    }
}
