using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using iazfitcoach.Models;
using iazfitcoach.Services;

namespace iazfitcoach.Controllers;

[ApiController]
[Route("api")]
public class ChatController : ControllerBase
{
    private readonly IGeminiService _gemini;
    private readonly IVectorStoreService _vectorStore;
    private readonly ITelemetryService _telemetry;
    private readonly RagOptions _ragOpts;
    private readonly ILogger<ChatController> _log;

    public ChatController(
        IGeminiService gemini,
        IVectorStoreService vectorStore,
        ITelemetryService telemetry,
        IOptions<RagOptions> ragOpts,
        ILogger<ChatController> logger)
    {
        _gemini = gemini;
        _vectorStore = vectorStore;
        _telemetry = telemetry;
        _ragOpts = ragOpts.Value;
        _log = logger;
    }

    /// <summary>
    /// POST /api/chat/ask
    /// RAG-powered chat endpoint with telemetry and memory history.
    /// </summary>
    [HttpPost("chat/ask")]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "A pergunta não pode ser vazia." });
        }

        var sw = Stopwatch.StartNew();

        // 1. Retrieve relevant context chunks via vector search
        var searchResults = (await _vectorStore.SearchAsync(request.Question, _ragOpts.TopK)).ToList();

        double maxSimilarity = 0.0;
        string topSource = string.Empty;
        var contextBuilder = new StringBuilder();

        if (searchResults.Count > 0)
        {
            maxSimilarity = searchResults.Max(r => r.Similarity);
            var bestResult = searchResults.OrderByDescending(r => r.Similarity).First();
            topSource = bestResult.Chunk.SourceFile;

            contextBuilder.AppendLine("Contexto recuperado da base de conhecimento oficial:");
            foreach (var res in searchResults)
            {
                contextBuilder.AppendLine($"- [{res.Chunk.SourceFile}] (Similaridade: {res.Similarity:P0}): {res.Chunk.Text}");
            }
        }
        else
        {
            contextBuilder.AppendLine("Nenhum contexto relevante encontrado na base de conhecimento.");
        }

        bool contextFound = searchResults.Count > 0 && maxSimilarity >= _ragOpts.MinSimilarity;

        // 2. Build system prompt including RAG context
        var systemPrompt = $"{_ragOpts.SystemPrompt}\n\n{contextBuilder}";

        // 3. Format conversation history if provided (last 4 messages)
        var userFullPrompt = new StringBuilder();
        if (request.History != null && request.History.Count > 0)
        {
            userFullPrompt.AppendLine("Histórico da conversa recente:");
            var recentHistory = request.History.TakeLast(4);
            foreach (var msg in recentHistory)
            {
                userFullPrompt.AppendLine($"{msg.Role}: {msg.Content}");
            }
            userFullPrompt.AppendLine();
        }
        userFullPrompt.AppendLine($"Pergunta atual do aluno: {request.Question}");

        // 4. Call Gemini Chat
        string answer;
        try
        {
            answer = await _gemini.ChatAsync(systemPrompt, userFullPrompt.ToString(), ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Erro ao chamar o serviço Gemini no chat.");
            return StatusCode(500, new { error = "Erro interno ao processar a resposta do assistente. Verifique se a chave de API do Gemini está configurada." });
        }

        sw.Stop();
        var latencyMs = sw.ElapsedMilliseconds;

        // Rough token estimation (approx 4 chars per token)
        int tokensEstimated = (request.Question.Length + answer.Length) / 4;

        // 5. Record telemetry
        _telemetry.Record(new InteractionMetric
        {
            Timestamp = DateTime.UtcNow,
            Prompt = request.Question,
            LatencyMs = latencyMs,
            TokensEstimated = tokensEstimated,
            MaxCosineSimilarity = maxSimilarity,
            ContextFound = contextFound,
            TopSource = topSource
        });

        return Ok(new
        {
            answer,
            metadata = new
            {
                latencyMs,
                similarityScore = Math.Round(maxSimilarity, 3),
                contextFound,
                topSource,
                chunksUsed = searchResults.Count
            }
        });
    }

    /// <summary>
    /// GET /api/metrics/drift
    /// Surfaces interactions with low similarity (&lt; 0.65), signaling knowledge gaps or data drift.
    /// </summary>
    [HttpGet("metrics/drift")]
    public IActionResult GetDrift()
    {
        var metrics = _telemetry.Snapshot().ToList();
        var drifts = metrics
            .Where(m => !m.ContextFound || m.MaxCosineSimilarity < 0.65)
            .OrderByDescending(m => m.Timestamp)
            .ToList();

        return Ok(new
        {
            totalInteractions = metrics.Count,
            driftCount = drifts.Count,
            drifts
        });
    }

    /// <summary>
    /// GET /api/metrics/summary
    /// General telemetry summary.
    /// </summary>
    [HttpGet("metrics/summary")]
    public IActionResult GetSummary()
    {
        var metrics = _telemetry.Snapshot().ToList();
        if (metrics.Count == 0)
        {
            return Ok(new { total = 0, avgLatencyMs = 0.0, avgSimilarity = 0.0 });
        }

        return Ok(new
        {
            total = metrics.Count,
            avgLatencyMs = metrics.Average(m => m.LatencyMs),
            avgSimilarity = metrics.Average(m => m.MaxCosineSimilarity),
            recent = metrics.Take(10)
        });
    }
}