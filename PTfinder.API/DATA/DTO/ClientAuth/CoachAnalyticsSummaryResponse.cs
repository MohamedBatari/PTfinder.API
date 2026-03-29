namespace PTfinder.API.DATA.DTO.ClientAuth
{
    public class CoachAnalyticsSummaryResponse
    {
        public int CoachId { get; set; }

        public int TotalProfileViews { get; set; }
        public int UniqueVisitors { get; set; }
        public int SignedInProfileViews { get; set; }
        public int AnonymousProfileViews { get; set; }

        public int WhatsappClicks { get; set; }
        public int EmailClicks { get; set; }
        public int PhoneClicks { get; set; }

        public int TotalContactClicks =>
            WhatsappClicks + EmailClicks + PhoneClicks;
    }
}
