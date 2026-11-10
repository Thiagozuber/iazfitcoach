using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using iazfitcoach.Models;

namespace iazfitcoach.Services;

public interface IGeminiService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);

    string ResolveApiKey();
}

public class GeminiService : IGeminiService
{
    private readonly HttpClient _http;
    private readonly ILogger<GeminiService> _log;
    private readonly GeminiOptions _opts;

    public GeminiService(
        HttpClient http,
        ILogger<GeminiService> log,
        IOptions<GeminiOptions> opts)
    {
        _http = http;
        _log = log;
        _opts = opts.Value;
    }

    public string ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("Gemini__ApiKey");

        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var cfg = _opts.ApiKey;

        if (!string.IsNullOrWhiteSpace(cfg))
            return cfg.Trim();

        throw new InvalidOperationException(
            "Gemini API key not configured.");
    }

    // ==========================================
    // EMBEDDINGS - RAG
    // ==========================================

    public async Task<float[]> EmbedAsync(
        string text,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();

        var model = _opts.EmbeddingModel;

        var modelResourceName = model.StartsWith(
            "models/",
            StringComparison.OrdinalIgnoreCase)
                ? model
                : $"models/{model}";

        var payload = new
        {
            model = modelResourceName,
            content = new
            {
                parts = new[]
                {
                    new { text }
                }
            }
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1/{modelResourceName}:embedContent")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };

        req.Headers.Add("x-goog-api-key", apiKey);

        using var resp = await _http.SendAsync(req, ct);

        var responseBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError(
                "Gemini Embedding retornou HTTP {StatusCode}",
                (int)resp.StatusCode);

            throw new InvalidOperationException(
                $"Gemini embedding failed with HTTP {(int)resp.StatusCode}. " +
                $"Detalhes: {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);

        var embedding = doc.RootElement
            .GetProperty("embedding")
            .GetProperty("values");

        var vector = new float[embedding.GetArrayLength()];

        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = embedding[i].GetSingle();
        }

        return vector;
    }

    // ==========================================
    // CHAT - GEMINI INTERACTIONS API
    // ==========================================

    public async Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();

        var model = _opts.Model.Replace(
            "models/",
            "",
            StringComparison.OrdinalIgnoreCase);

        // Se o systemPrompt vier vazio ou genérico, aplicamos a nossa diretriz de hierarquia inteligente
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            systemPrompt = 
                "Você é o iazfitcoach, assistente técnico oficial de treino e nutrição. " +
                "1. Priorize sempre responder com base estrita nos documentos de contexto fornecidos. " +
                "2. Caso a informação exata não esteja nos documentos locais, utilize seu conhecimento técnico e científico geral em treinamento, fisiologia e nutrição para responder à dúvida do usuário com precisão (por exemplo, cálculo de macros de alimentos comuns como pão e ovos). " +
                "3. Seja claro e prestativo, mantendo um tom profissional de coach.";
        }
        else
        {
            // Garante que mesmo com contexto customizado, ele saiba usar o fallback interno caso o documento não cubra
            systemPrompt += "\n\n[Diretriz de Fallback]: Se a informação específica não estiver nos documentos de contexto acima, utilize seu conhecimento técnico geral para responder com precisão ao usuário.";
        }

        var payload = new
        {
            model = model,
            input = userPrompt,
            system_instruction = systemPrompt,
            generation_config = new
            {
                temperature = _opts.Temperature,
                max_output_tokens = _opts.MaxTokens
            }
        };

        var json = JsonSerializer.Serialize(payload);

        const int maxAttempts = 4;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                "https://generativelanguage.googleapis.com/v1beta/interactions")
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };

            req.Headers.Add("x-goog-api-key", apiKey);

            using var resp = await _http.SendAsync(req, ct);

            var responseBody = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);

                var root = doc.RootElement;

                if (!root.TryGetProperty("steps", out var steps))
                {
                    throw new InvalidOperationException(
                        "Gemini retornou uma resposta sem steps.");
                }

                var sb = new StringBuilder();

                foreach (var step in steps.EnumerateArray())
                {
                    if (!step.TryGetProperty("type", out var type))
                        continue;

                    if (type.GetString() != "model_output")
                        continue;

                    if (!step.TryGetProperty("content", out var content))
                        continue;

                    foreach (var item in content.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var itemType) &&
                            itemType.GetString() == "text" &&
                            item.TryGetProperty("text", out var text))
                        {
                            sb.Append(text.GetString());
                        }
                    }
                }

                var result = sb.ToString();

                if (string.IsNullOrWhiteSpace(result))
                {
                    throw new InvalidOperationException(
                        "Gemini concluiu a interação, mas não retornou texto.");
                }

                _log.LogInformation(
                    "Gemini respondeu com sucesso na tentativa {Attempt}.",
                    attempt);

                return result;
            }

            var statusCode = (int)resp.StatusCode;

            bool shouldRetry =
                statusCode == 429 ||
                statusCode == 500 ||
                statusCode == 502 ||
                statusCode == 503 ||
                statusCode == 504;

            _log.LogWarning(
                "Gemini HTTP {StatusCode}. Tentativa {Attempt}/{MaxAttempts}.",
                statusCode,
                attempt,
                maxAttempts);

            if (!shouldRetry || attempt == maxAttempts)
            {
                throw new InvalidOperationException(
                    $"Gemini chat failed with HTTP {statusCode}. " +
                    $"Detalhes: {responseBody}");
            }

            var delaySeconds = Math.Pow(2, attempt);

            _log.LogInformation(
                "Nova tentativa Gemini em {Delay} segundos.",
                delaySeconds);

            await Task.Delay(
                TimeSpan.FromSeconds(delaySeconds),
                ct);
        }

        throw new InvalidOperationException(
            "Não foi possível obter resposta do Gemini.");
    }
}

// ==========================================
// CONFIGURAÇÕES GEMINI
// ==========================================

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-3.5-flash"; // ou o modelo que estiver usando

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    public int MaxTokens { get; set; } = 4096;

    public double Temperature { get; set; } = 0.2;
}