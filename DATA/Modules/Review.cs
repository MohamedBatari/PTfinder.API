// PTfinder.API.DATA.Modules.Review (entity)
public class Review
{
    public int Id { get; set; }
    public int CoachId { get; set; }

    public string StudentName { get; set; } = default!;
    public string Comment { get; set; } = default!;
    public int Rating { get; set; }

    // NEW
    public string? StudentEmail { get; set; }
    public string? GoogleSub { get; set; }
    public string? AvatarUrl { get; set; }
    public bool GoogleVerified { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

