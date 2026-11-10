namespace iazfitcoach.Models;

/// <summary>
/// Single telemetry record for one RAG interaction.
/// All fields are intentionally primitive so the data can be serialized
/// to any observability backend without extra mapping.
/// </summary>
public class InteractionMetric
{
    public DateTime Timestamp { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public double LatencyMs { get; set; }
    public int TokensEstimated { get; set; }
    public double MaxCosineSimilarity { get; set; }
    public bool ContextFound { get; set; }
    public string TopSource { get; set; } = string.Empty;
}