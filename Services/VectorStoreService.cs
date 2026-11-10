using System.Text.RegularExpressions;
using iazfitcoach.Models;
using Microsoft.Extensions.Options;

namespace iazfitcoach.Services;

public class SearchResult
{
    public DocumentChunk Chunk { get; set; } = new();
    public double Similarity { get; set; }
}

public interface IVectorStoreService
{
    Task IngestAsync();
    Task<IEnumerable<SearchResult>> SearchAsync(string query, int topK);
    IEnumerable<DocumentChunk> AllChunks { get; }
}

public class VectorStoreService : IVectorStoreService
{
    private readonly IGeminiService _gemini;
    private readonly RagOptions _opts;
    private readonly ILogger<VectorStoreService> _log;
    private readonly List<DocumentChunk> _chunks = new();

    public VectorStoreService(
        IGeminiService gemini,
        IOptions<RagOptions> opts,
        ILogger<VectorStoreService> log)
    {
        _gemini = gemini;
        _opts = opts.Value;
        _log = log;
    }

    public IEnumerable<DocumentChunk> AllChunks => _chunks;

    public async Task IngestAsync()
    {
        var docsFolder = Path.Combine(
            Directory.GetCurrentDirectory(),
            _opts.DocsFolder);

        if (!Directory.Exists(docsFolder))
        {
            _log.LogWarning("Docs folder not found: {Folder}", docsFolder);
            return;
        }

        var files = Directory.GetFiles(docsFolder, "*.md", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            _log.LogWarning("No Markdown files found in {Folder}", docsFolder);
            return;
        }

        _log.LogInformation("Ingesting {Count} Markdown files", files.Length);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var text = await File.ReadAllTextAsync(file);
            var chunks = SplitIntoChunks(text, fileName);

            foreach (var chunk in chunks)
            {
                try
                {
                    var embedding = await _gemini.EmbedAsync(chunk.Text);
                    _chunks.Add(new DocumentChunk
                    {
                        SourceFile = fileName,
                        Text = chunk.Text,
                        Embedding = embedding
                    });
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to embed chunk from {File}", fileName);
                }
            }
        }

        _log.LogInformation("Ingested {Count} chunks from {Files} files", _chunks.Count, files.Length);
    }

    public async Task<IEnumerable<SearchResult>> SearchAsync(string query, int topK)
    {
        if (_chunks.Count == 0)
        {
            _log.LogWarning("No chunks available for search");
            return Enumerable.Empty<SearchResult>();
        }

        var queryEmbedding = await _gemini.EmbedAsync(query);

        var results = _chunks
            .Select(chunk => new SearchResult
            {
                Chunk = chunk,
                Similarity = CosineSimilarity(queryEmbedding, chunk.Embedding)
            })
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .ToList();

        return results;
    }

    private static IEnumerable<(string Text, string File)> SplitIntoChunks(string text, string fileName)
    {
        var paragraphs = Regex.Split(text, @"\r?\n\r?\n+")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim());

        foreach (var paragraph in paragraphs)
        {
            // Yield non-empty paragraphs as chunks (keeps markdown lists together)
            if (paragraph.Length > 20)
            {
                yield return (paragraph, fileName);
            }
        }
    }

    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length == 0 || b.Length == 0 || a.Length != b.Length)
            return 0.0;

        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0 || normB <= 0)
            return 0.0;

        return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}