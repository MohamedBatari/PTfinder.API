namespace PTfinder.API.DTO.Conversations
{
    public sealed class StartConversationRequest
    {
        public int CoachId { get; set; }
        public string? Message { get; set; }
    }

    public sealed class SendConversationMessageRequest
    {
        public string? Message { get; set; }
    }
}
