namespace iazfitcoach.Models;

/// <summary>
/// Represents a single chunk of ingested knowledge (a Markdown sentence/paragraph)
/// together with its embedding vector and metadata used by the RAG pipeline.
/// </summary>
public class DocumentChunk
{
    public int Id { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;
}