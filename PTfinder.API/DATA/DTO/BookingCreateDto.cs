namespace PTfinder.API.DATA.DTO
{
    public class BookingCreateDto
    {
        public int CoachId { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public string StudentPhone { get; set; }
        public DateTime BookingDate { get; set; }
        public string TimeSlot { get; set; }
    }
}
