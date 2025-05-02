namespace PTfinder.API.DATA.Modules
{
    public class Availability
    {
        public int Id { get; set; }
        public int CoachId { get; set; }
        public Coach Coach { get; set; }
        public DateTime AvailableDate { get; set; }
        public string TimeSlot { get; set; } // e.g., "10:00 - 11:00"
    }
}
