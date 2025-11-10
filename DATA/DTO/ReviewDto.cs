public class ReviewDto
{
    public int Id { get; set; }
    public string StudentName { get; set; } = default!;
    public string? StudentEmail { get; set; }
    public string Comment { get; set; } = default!;
    public int Rating { get; set; }

    // extra fields for the UI
    public bool GoogleVerified { get; set; }
    public string? Avatar { get; set; }
    public DateTime CreatedAt { get; set; }
}
