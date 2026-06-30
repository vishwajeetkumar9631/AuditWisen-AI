using AuditWiseAI.Models;
using AuditWiseAI.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
const string FrontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration
    .GetSection("Frontend:AllowedOrigins")
    .Get<string[]>() ??
[
    "http://localhost:3000",
    "http://localhost:5173",
    "http://localhost:5174",
    "http://127.0.0.1:3000",
    "http://127.0.0.1:5173",
    "http://127.0.0.1:5174"
];

builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<OpenAiRagOptions>(builder.Configuration.GetSection("OpenAI:Rag"));
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = AuditFileLimits.MaxUploadBytes;
    options.ValueLengthLimit = AuditFileLimits.MaxPayloadCharacters;
});
builder.Services.AddHttpClient("webhooks", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient("openai", client => client.Timeout = TimeSpan.FromSeconds(60));

builder.Services.AddSingleton<AuditDatabase>();
builder.Services.AddSingleton<IAuditRepository, LiteDbAuditRepository>();
builder.Services.AddSingleton<IAuditJobQueue, AuditJobQueue>();
builder.Services.AddSingleton<IDocumentChunker, SlidingWindowDocumentChunker>();
builder.Services.AddSingleton<ILlmDocumentChunker, OpenAiDocumentChunker>();
builder.Services.AddSingleton<ITextEmbeddingService, OpenAiTextEmbeddingService>();
builder.Services.AddSingleton<IAuditReasoningService, OpenAiAuditReasoningService>();
builder.Services.AddSingleton<PolicyEmbeddingCache>();
builder.Services.AddSingleton<IPolicyCatalog, LiteDbPolicyCatalog>();
builder.Services.AddSingleton<IPolicyRetrievalService, DatabasePolicyRetrievalService>();
builder.Services.AddSingleton<ISemanticCache, InMemorySemanticCache>();
builder.Services.AddSingleton<IComplianceAnalysisService, ComplianceAnalysisService>();
builder.Services.AddSingleton<IDocumentChatService, DocumentChatService>();
builder.Services.AddSingleton<IWebhookDispatcher, LoggingWebhookDispatcher>();
builder.Services.AddSingleton<IAuditRealtimeNotifier, SignalRAuditRealtimeNotifier>();
builder.Services.AddSingleton<IOcrTextExtractor, TesseractOcrTextExtractor>();
builder.Services.AddSingleton<IAuditFileReader, MultipartAuditFileReader>();
builder.Services.AddHostedService<AuditWorker>();

var app = builder.Build();
app.Services.GetRequiredService<AuditDatabase>().Initialize();
app.UseCors(FrontendCorsPolicy);

app.MapHub<AuditHub>("/hubs/audits");

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    service = "AuditWise AI",
    status = "healthy",
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/api/db/status", (AuditDatabase database) => Results.Ok(new
{
    provider = "LiteDB",
    database.DatabasePath,
    exists = File.Exists(database.DatabasePath),
    auditCount = database.Audits.Count(),
    policyCount = database.Policies.Count()
}));

app.MapGet("/api/llm/status", async (
    ITextEmbeddingService embeddingService,
    IOptions<OpenAiRagOptions> ragOptions,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var options = ragOptions.Value;
    var apiKey = LlmProviderConfiguration.GetApiKey(options, configuration);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Ok(new
        {
            configured = false,
            working = false,
            reason = LlmProviderConfiguration.UsesGroq(options)
                ? "GROQ_API_KEY or Groq:ApiKey is not configured for this process."
                : "OPENAI_API_KEY or OpenAI:ApiKey is not configured for this process.",
            options.Provider,
            baseUrl = LlmProviderConfiguration.GetBaseUrl(options),
            options.EmbeddingModel,
            options.ChunkingModel,
            options.ReasoningModel,
            options.EmbeddingDimensions,
            embeddingConfigured = embeddingService.IsConfigured
        });
    }

    try
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{LlmProviderConfiguration.GetBaseUrl(options)}/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = JsonContent.Create(new
        {
            model = options.ReasoningModel,
            input = "Reply with exactly: ok"
        });

        using var client = httpClientFactory.CreateClient("openai");
        using var response = await client.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Results.Ok(new
            {
                configured = true,
                working = false,
                error = $"LLM status request failed with {(int)response.StatusCode}: {responseBody}",
                options.Provider,
                baseUrl = LlmProviderConfiguration.GetBaseUrl(options),
                options.EmbeddingModel,
                options.ChunkingModel,
                options.ReasoningModel,
                options.EmbeddingDimensions,
                embeddingConfigured = embeddingService.IsConfigured
            });
        }

        var outputText = ExtractResponseText(responseBody);

        return Results.Ok(new
        {
            configured = true,
            working = !string.IsNullOrWhiteSpace(outputText),
            outputText,
            options.Provider,
            baseUrl = LlmProviderConfiguration.GetBaseUrl(options),
            options.EmbeddingModel,
            options.ChunkingModel,
            options.ReasoningModel,
            options.EmbeddingDimensions,
            embeddingConfigured = embeddingService.IsConfigured
        });
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        return Results.Ok(new
        {
            configured = true,
            working = false,
            error = exception.Message,
            options.Provider,
            baseUrl = LlmProviderConfiguration.GetBaseUrl(options),
            options.EmbeddingModel,
            options.ChunkingModel,
            options.ReasoningModel,
            options.EmbeddingDimensions,
            embeddingConfigured = embeddingService.IsConfigured
        });
    }
});

app.MapGet("/api/policies", async (
    IPolicyCatalog policyCatalog,
    CancellationToken cancellationToken) =>
{
    var policies = await policyCatalog.GetAllAsync(cancellationToken);
    return Results.Ok(policies);
});

app.MapGet("/api/audits", async (
    int? take,
    IAuditRepository repository,
    CancellationToken cancellationToken) =>
{
    var records = await repository.ListAsync(take ?? 20, cancellationToken);
    return Results.Ok(records);
});

app.MapPost("/api/audits", async (
    AuditRequest request,
    IAuditRepository repository,
    IAuditJobQueue queue,
    IAuditRealtimeNotifier realtimeNotifier,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var validationProblem = Validate(request);
    if (validationProblem is not null)
    {
        return validationProblem;
    }

    var record = await repository.CreateAsync(request, cancellationToken);
    await realtimeNotifier.PublishAsync(record, cancellationToken);
    await queue.EnqueueAsync(new QueuedAudit(record.Id), cancellationToken);

    var statusUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/audits/{record.Id}";
    var response = new AuditAcceptedResponse(record.Id, record.Status, statusUrl, record.CreatedAt);

    return Results.Accepted(statusUrl, response);
});

app.MapPost("/api/audits/files", async (
    HttpRequest httpRequest,
    IAuditFileReader fileReader,
    IAuditRepository repository,
    IAuditJobQueue queue,
    IAuditRealtimeNotifier realtimeNotifier,
    CancellationToken cancellationToken) =>
{
    var parsed = await fileReader.ReadAsync(httpRequest, cancellationToken);
    if (!parsed.Succeeded)
    {
        return Results.ValidationProblem(parsed.Errors);
    }

    var record = await repository.CreateAsync(parsed.Request!, cancellationToken);
    await realtimeNotifier.PublishAsync(record, cancellationToken);
    await queue.EnqueueAsync(new QueuedAudit(record.Id), cancellationToken);

    var statusUrl = $"{httpRequest.Scheme}://{httpRequest.Host}/api/audits/{record.Id}";
    var response = new AuditAcceptedResponse(record.Id, record.Status, statusUrl, record.CreatedAt);

    return Results.Accepted(statusUrl, response);
});

app.MapPost("/api/audits/analyze-sync", async (
    AuditRequest request,
    IComplianceAnalysisService analysisService,
    CancellationToken cancellationToken) =>
{
    var validationProblem = Validate(request);
    if (validationProblem is not null)
    {
        return validationProblem;
    }

    var result = await analysisService.AnalyzeAsync(request, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/audits/files/analyze-sync", async (
    HttpRequest httpRequest,
    IAuditFileReader fileReader,
    IComplianceAnalysisService analysisService,
    CancellationToken cancellationToken) =>
{
    var parsed = await fileReader.ReadAsync(httpRequest, cancellationToken);
    if (!parsed.Succeeded)
    {
        return Results.ValidationProblem(parsed.Errors);
    }

    var result = await analysisService.AnalyzeAsync(parsed.Request!, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/audits/{id:guid}", async (
    Guid id,
    IAuditRepository repository,
    CancellationToken cancellationToken) =>
{
    var record = await repository.GetAsync(id, cancellationToken);
    return record is null ? Results.NotFound() : Results.Ok(record);
});

app.MapPost("/api/audits/{id:guid}/chat", async (
    Guid id,
    AuditChatRequest request,
    IDocumentChatService chatService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.EffectiveMessage))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.Message)] = ["Message is required."]
        });
    }

    var response = await chatService.ChatAsync(id, request, cancellationToken);
    return response is null ? Results.NotFound() : Results.Ok(response);
});

app.Run();

static IResult? Validate(AuditRequest request)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.Payload))
    {
        errors[nameof(request.Payload)] = ["Payload is required."];
    }
    else if (request.Payload.Length > AuditFileLimits.MaxPayloadCharacters)
    {
        errors[nameof(request.Payload)] = [$"Payload must be {AuditFileLimits.MaxPayloadCharacters:N0} characters or fewer."];
    }

    if (string.IsNullOrWhiteSpace(request.ContentType))
    {
        errors[nameof(request.ContentType)] = ["Content type is required."];
    }

    return errors.Count == 0 ? null : Results.ValidationProblem(errors);
}

static string ExtractResponseText(string responseBody)
{
    using var document = JsonDocument.Parse(responseBody);
    if (document.RootElement.TryGetProperty("output_text", out var outputText))
    {
        return outputText.GetString() ?? string.Empty;
    }

    var builder = new System.Text.StringBuilder();
    if (document.RootElement.TryGetProperty("output", out var output))
    {
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text))
                {
                    builder.Append(text.GetString());
                }
            }
        }
    }

    return builder.ToString();
}
