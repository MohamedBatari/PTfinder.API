public class ReviewCreateDto
{
    public int CoachId { get; set; }
    public string Comment { get; set; } = default!;
    public int Rating { get; set; } // 1..5
    public string GoogleIdToken { get; set; } = default!; // <-- required
}
