using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuditWiseAI.Models;
using Microsoft.Extensions.Options;

namespace AuditWiseAI.Services;

public sealed class OpenAiRagOptions
{
    public string Provider { get; set; } = "Groq";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string ChunkingModel { get; set; } = "llama-3.3-70b-versatile";
    public string ReasoningModel { get; set; } = "llama-3.3-70b-versatile";
    public int EmbeddingDimensions { get; set; } = 512;
    public int MaxLlmChunkingCharacters { get; set; } = 60_000;
    public int MaxReasoningCharacters { get; set; } = 80_000;
}

public static class LlmProviderConfiguration
{
    public static bool UsesGroq(OpenAiRagOptions options) =>
        options.Provider.Equals("Groq", StringComparison.OrdinalIgnoreCase);

    public static bool UsesOpenAi(OpenAiRagOptions options) =>
        options.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);

    public static string GetBaseUrl(OpenAiRagOptions options)
    {
        var providerDefault = UsesGroq(options)
            ? "https://api.groq.com/openai/v1"
            : "https://api.openai.com/v1";

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return providerDefault;
        }

        var baseUrl = options.BaseUrl.Trim().TrimEnd('/');
        if (UsesGroq(options) && baseUrl.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase))
        {
            return providerDefault;
        }

        if (UsesOpenAi(options) && baseUrl.Contains("api.groq.com", StringComparison.OrdinalIgnoreCase))
        {
            return providerDefault;
        }

        return baseUrl;
    }

    public static string? GetApiKey(OpenAiRagOptions options, IConfiguration configuration)
    {
        if (UsesGroq(options))
        {
            return configuration["Groq:ApiKey"] ??
                   configuration["GROQ_API_KEY"] ??
                   Environment.GetEnvironmentVariable("GROQ_API_KEY");
        }

        return GetOpenAiApiKey(configuration);
    }

    public static string? GetOpenAiEmbeddingApiKey(IConfiguration configuration) =>
        GetOpenAiApiKey(configuration);

    private static string? GetOpenAiApiKey(IConfiguration configuration)
    {
        var apiKey = FirstConfiguredValue(
            configuration["OpenAI:ApiKey"],
            configuration["OPENAI_API_KEY"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

        return IsGroqApiKey(apiKey) ? null : apiKey;
    }

    private static string? FirstConfiguredValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool IsGroqApiKey(string? apiKey) =>
        apiKey?.StartsWith("gsk_", StringComparison.OrdinalIgnoreCase) == true;
}

public interface ITextEmbeddingService
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}

public sealed class OpenAiTextEmbeddingService(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiRagOptions> options,
    IConfiguration configuration) : ITextEmbeddingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiRagOptions _options = options.Value;

    public bool IsConfigured => LlmProviderConfiguration.UsesOpenAi(_options) &&
        !string.IsNullOrWhiteSpace(LlmProviderConfiguration.GetOpenAiEmbeddingApiKey(configuration));

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("OpenAI embeddings are not configured for this process.");
        }

        var request = new
        {
            model = _options.EmbeddingModel,
            input = inputs,
            dimensions = _options.EmbeddingDimensions,
            encoding_format = "float"
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LlmProviderConfiguration.GetOpenAiEmbeddingApiKey(configuration));
        message.Content = JsonContent.Create(request);

        using var client = httpClientFactory.CreateClient("openai");
        using var response = await client.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Embedding request failed with {(int)response.StatusCode}: {responseBody}");
        }

        var payload = JsonSerializer.Deserialize<EmbeddingResponse>(responseBody, SerializerOptions) ??
            throw new InvalidOperationException("Embedding response was empty.");

        return payload.Data
            .OrderBy(item => item.Index)
            .Select(item => item.Embedding)
            .ToArray();
    }

    private sealed record EmbeddingResponse(IReadOnlyList<EmbeddingItem> Data);

    private sealed record EmbeddingItem(int Index, float[] Embedding);
}

public interface ILlmDocumentChunker
{
    Task<IReadOnlyList<DocumentChunk>?> TryChunkAsync(string document, CancellationToken cancellationToken);
}

public sealed class OpenAiDocumentChunker(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiRagOptions> options,
    IConfiguration configuration,
    ILogger<OpenAiDocumentChunker> logger) : ILlmDocumentChunker
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly OpenAiRagOptions _options = options.Value;

    public async Task<IReadOnlyList<DocumentChunk>?> TryChunkAsync(string document, CancellationToken cancellationToken)
    {
        var apiKey = LlmProviderConfiguration.GetApiKey(_options, configuration);
        if (string.IsNullOrWhiteSpace(apiKey) || document.Length > _options.MaxLlmChunkingCharacters)
        {
            return null;
        }

        var prompt = """
You split compliance, legal, audit, and security documents into retrieval chunks.
Return only JSON with this shape:
{"chunks":["chunk text 1","chunk text 2"]}

Rules:
- Preserve original wording.
- Keep clauses, lists, and evidence together.
- Each chunk should be 150-500 words when possible.
- Remove page headers, footers, and repeated boilerplate if obvious.
- Do not summarize.
""";

        var request = new
        {
            model = _options.ChunkingModel,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = prompt
                },
                new
                {
                    role = "user",
                    content = document
                }
            }
        };

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{LlmProviderConfiguration.GetBaseUrl(_options)}/responses");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Content = JsonContent.Create(request);

            using var client = httpClientFactory.CreateClient("openai");
            using var response = await client.SendAsync(message, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("LLM chunking failed with status {StatusCode}: {Response}", (int)response.StatusCode, responseBody);
                return null;
            }

            var outputText = ExtractResponseText(responseBody);
            var chunkResponse = JsonSerializer.Deserialize<ChunkResponse>(outputText, SerializerOptions);
            var chunks = chunkResponse?.Chunks?
                .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
                .Select((chunk, index) => new DocumentChunk(index, chunk.Trim(), EstimateTokens(chunk)))
                .ToArray();

            return chunks is { Length: > 0 } ? chunks : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "LLM chunking failed. Falling back to local sliding-window chunks.");
            return null;
        }
    }

    private static string ExtractResponseText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString() ?? string.Empty;
        }

        var builder = new StringBuilder();
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

    private static int EstimateTokens(string text) =>
        Math.Max(1, text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);

    private sealed record ChunkResponse([property: JsonPropertyName("chunks")] IReadOnlyList<string>? Chunks);
}

public sealed class PolicyEmbeddingCache
{
    private readonly ConcurrentDictionary<string, float[]> _cache = new(StringComparer.Ordinal);

    public bool TryGet(string key, out float[] vector) => _cache.TryGetValue(key, out vector!);

    public void Set(string key, float[] vector) => _cache[key] = vector;
}

public interface IAuditReasoningService
{
    Task<AuditResult?> TryEvaluateAsync(
        AuditRequest request,
        string documentIntent,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<PolicyReference> matchedPolicies,
        IReadOnlyList<ComplianceViolation> deterministicViolations,
        CancellationToken cancellationToken);
}

public sealed class OpenAiAuditReasoningService(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiRagOptions> options,
    IConfiguration configuration,
    ILogger<OpenAiAuditReasoningService> logger) : IAuditReasoningService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly OpenAiRagOptions _options = options.Value;

    public async Task<AuditResult?> TryEvaluateAsync(
        AuditRequest request,
        string documentIntent,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<PolicyReference> matchedPolicies,
        IReadOnlyList<ComplianceViolation> deterministicViolations,
        CancellationToken cancellationToken)
    {
        var apiKey = LlmProviderConfiguration.GetApiKey(_options, configuration);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var documentText = BuildDocumentEvidence(chunks);
        if (documentText.Length > _options.MaxReasoningCharacters)
        {
            documentText = documentText[.._options.MaxReasoningCharacters];
        }

        var requestBody = new
        {
            model = _options.ReasoningModel,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = """
You are AuditWise AI, an enterprise compliance audit reviewer.
Evaluate the document only against the supplied matched policies and evidence.
Return only valid JSON with this shape:
{
  "complianceStatus": "Passed|NeedsReview|Flagged",
  "riskScore": 0-99,
  "suggestedRemediation": "specific remediation text",
  "violations": [
    { "clause": "policy or document clause", "severity": "Low|Medium|High|Critical", "reason": "evidence-backed explanation" }
  ]
}

Rules:
- Do not invent policies that are not supplied.
- Prefer precise, audit-ready language.
- Include violations only when evidence is present in the document or deterministic findings.
- If deterministic findings are provided, preserve them unless clearly contradicted by evidence.
- Use Flagged for critical/high unresolved compliance issues, NeedsReview for medium/ambiguous issues, Passed only when no issue is present.
"""
                },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(new
                    {
                        documentIntent,
                        document = documentText,
                        matchedPolicies,
                        deterministicFindings = deterministicViolations
                    }, SerializerOptions)
                }
            }
        };

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{LlmProviderConfiguration.GetBaseUrl(_options)}/responses");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Content = JsonContent.Create(requestBody);

            using var client = httpClientFactory.CreateClient("openai");
            using var response = await client.SendAsync(message, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("LLM audit reasoning failed with status {StatusCode}: {Response}", (int)response.StatusCode, responseBody);
                return null;
            }

            var outputText = ExtractResponseText(responseBody);
            var llmResult = JsonSerializer.Deserialize<LlmAuditResult>(outputText, SerializerOptions);
            if (llmResult is null)
            {
                return null;
            }

            var llmViolations = llmResult.Violations?
                .Where(violation => !string.IsNullOrWhiteSpace(violation.Clause) && !string.IsNullOrWhiteSpace(violation.Reason))
                .Select(violation => new ComplianceViolation(
                    violation.Clause.Trim(),
                    violation.Severity,
                    violation.Reason.Trim()))
                .ToArray() ?? [];

            var mergedViolations = MergeViolations(llmViolations, deterministicViolations);
            var riskScore = Math.Clamp(llmResult.RiskScore, 0, 99);
            if (mergedViolations.Count > llmViolations.Length)
            {
                riskScore = Math.Max(riskScore, CalculateRiskFloor(mergedViolations));
            }

            return new AuditResult(
                NormalizeStatus(llmResult.ComplianceStatus, riskScore, mergedViolations),
                riskScore,
                mergedViolations,
                string.IsNullOrWhiteSpace(llmResult.SuggestedRemediation)
                    ? BuildFallbackRemediation(mergedViolations)
                    : llmResult.SuggestedRemediation.Trim(),
                documentIntent,
                matchedPolicies,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "LLM audit reasoning failed. Falling back to deterministic audit scoring.");
            return null;
        }
    }

    private static string BuildDocumentEvidence(IReadOnlyList<DocumentChunk> chunks)
    {
        var builder = new StringBuilder();
        foreach (var chunk in chunks.Take(12))
        {
            builder.AppendLine($"[Chunk {chunk.Index}]");
            builder.AppendLine(chunk.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ComplianceViolation> MergeViolations(
        IReadOnlyList<ComplianceViolation> llmViolations,
        IReadOnlyList<ComplianceViolation> deterministicViolations)
    {
        var merged = new List<ComplianceViolation>(llmViolations);
        foreach (var deterministic in deterministicViolations)
        {
            var exists = merged.Any(existing =>
                existing.Clause.Equals(deterministic.Clause, StringComparison.OrdinalIgnoreCase) ||
                existing.Reason.Contains(deterministic.Reason, StringComparison.OrdinalIgnoreCase) ||
                deterministic.Reason.Contains(existing.Reason, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                merged.Add(deterministic);
            }
        }

        return merged;
    }

    private static ComplianceStatus NormalizeStatus(string status, double riskScore, IReadOnlyList<ComplianceViolation> violations)
    {
        if (Enum.TryParse<ComplianceStatus>(status, ignoreCase: true, out var parsed))
        {
            if (parsed == ComplianceStatus.Passed && violations.Count > 0)
            {
                return riskScore >= 70 ? ComplianceStatus.Flagged : ComplianceStatus.NeedsReview;
            }

            return parsed;
        }

        return riskScore >= 70 ? ComplianceStatus.Flagged : violations.Count > 0 ? ComplianceStatus.NeedsReview : ComplianceStatus.Passed;
    }

    private static double CalculateRiskFloor(IReadOnlyList<ComplianceViolation> violations)
    {
        var score = violations.Sum(violation => violation.Severity switch
        {
            ViolationSeverity.Critical => 81,
            ViolationSeverity.High => 34,
            ViolationSeverity.Medium => 16,
            _ => 8
        });

        return Math.Min(99, score + (violations.Count * 3.5));
    }

    private static string BuildFallbackRemediation(IReadOnlyList<ComplianceViolation> violations)
    {
        return violations.Count == 0
            ? "No remediation required. Retain evidence of this automated review."
            : "Review the listed findings, update the document evidence, and re-run the audit before approval.";
    }

    private static string ExtractResponseText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString() ?? string.Empty;
        }

        var builder = new StringBuilder();
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

    private sealed record LlmAuditResult(
        string ComplianceStatus,
        double RiskScore,
        string SuggestedRemediation,
        IReadOnlyList<LlmViolation>? Violations);

    private sealed record LlmViolation(
        string Clause,
        ViolationSeverity Severity,
        string Reason);
}
