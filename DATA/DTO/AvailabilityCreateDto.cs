namespace PTfinder.API.DATA.DTO
{
    public class AvailabilityCreateDto
    {
        public int CoachId { get; set; }
        public DateTime AvailableDate { get; set; }
        public string TimeSlot { get; set; }
    }
}
