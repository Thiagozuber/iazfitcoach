using Microsoft.Extensions.Options;
using iazfitcoach.Services;
using iazfitcoach.Models;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Configuration sections
// ------------------------------------------------------------------
builder.Services.Configure<GeminiOptions>(
    builder.Configuration.GetSection(GeminiOptions.SectionName));

builder.Services.Configure<RagOptions>(
    builder.Configuration.GetSection(RagOptions.SectionName));

// ------------------------------------------------------------------
// Typed HttpClient via IHttpClientFactory
// ------------------------------------------------------------------
builder.Services.AddHttpClient<IGeminiService, GeminiService>((sp, http) =>
{
    http.Timeout = TimeSpan.FromMinutes(2);
});

// ------------------------------------------------------------------
// Application services (Singleton RAG store + telemetry)
// ------------------------------------------------------------------
builder.Services.AddSingleton<IVectorStoreService, VectorStoreService>();
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "iazfitcoach API",
        Version = "v1",
        Description = "RAG-powered fitness coaching assistant."
    });
});

// ------------------------------------------------------------------
// CORS
// ------------------------------------------------------------------
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin()
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

// ------------------------------------------------------------------
// HTTP pipeline
// ------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// ORDEM CORRETA: DefaultFiles ANTES de StaticFiles
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// ------------------------------------------------------------------
// Bootstrap the RAG knowledge base at startup
// ------------------------------------------------------------------
var store = app.Services.GetRequiredService<IVectorStoreService>();
try
{
    await store.IngestAsync();
    Console.WriteLine(">>> RAG INGESTION CONCLUÍDA COM SUCESSO! <<<");
}
catch (Exception ex)
{
    Console.WriteLine($">>> ERRO CRÍTICO NA INGESTÃO: {ex.Message} <<<");
}

app.Run();