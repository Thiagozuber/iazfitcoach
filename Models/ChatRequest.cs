namespace iazfitcoach.Models;

/// <summary>Request body for POST /api/chat/ask.</summary>
public class ChatRequest
{
    public string Question { get; set; } = string.Empty;
    public List<ChatMessage>? History { get; set; }
}

/// <summary>Single conversational message exchanged with the coach.</summary>
public class ChatMessage
{
    public string Role { get; set; } = "user"; // "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}