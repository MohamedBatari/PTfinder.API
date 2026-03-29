namespace PTfinder.API.DATA.DTO.ClientAuth
{
    public class TrackCoachProfileViewRequest
    {

        
        
            public string? SessionId { get; set; }
            public string? ViewSource { get; set; } = "coach_page";
            public string? ClientTimeZone { get; set; }
        
    }
}
