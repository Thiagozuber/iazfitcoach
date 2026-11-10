namespace iazfitcoach.Models;

/// <summary>
/// Configuration section "Rag" in appsettings.json.
/// </summary>
public class RagOptions
{
    public const string SectionName = "Rag";
    public double MinSimilarity { get; set; } = 0.65;
    public int TopK { get; set; } = 4;
    public string DocsFolder { get; set; } = "Data/Docs";
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;

    public const string DefaultSystemPrompt =
        "Você é o iazfitcoach, assistente técnico oficial de treino. " +
        "Responda em português, de forma clara, técnica e prática. " +
        "Priorize estritamente as informações contidas no contexto de recuperação (RAG) fornecido. " +
        "Se o contexto não for suficiente, diga isso explicitamente e não invente dados. " +
        "Use formatação Markdown e seja conciso.";
}