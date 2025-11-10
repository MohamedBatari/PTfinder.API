using PTfinder.API.Enums;

namespace PTfinder.API.DATA.Modules
{
    public class Booking
    {
        public int Id { get; set; }
        public int CoachId { get; set; }
        public Coach Coach { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public string StudentPhone { get; set; }
        public DateTime BookingDate { get; set; }
        public string TimeSlot { get; set; }
        public BookingStatus Status { get; set; }
    }
}
