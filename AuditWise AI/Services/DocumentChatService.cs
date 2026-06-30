using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AuditWiseAI.Models;
using Microsoft.Extensions.Options;

namespace AuditWiseAI.Services;

public interface IDocumentChatService
{
    Task<AuditChatResponse?> ChatAsync(Guid auditId, AuditChatRequest request, CancellationToken cancellationToken);
}

public sealed class DocumentChatService(
    IAuditRepository repository,
    IDocumentChunker chunker,
    ILlmDocumentChunker llmChunker,
    ITextEmbeddingService embeddingService,
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiRagOptions> options,
    IConfiguration configuration,
    ILogger<DocumentChatService> logger) : IDocumentChatService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Regex TokenPattern = new("[a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "in", "is", "it",
        "must", "of", "on", "or", "that", "the", "this", "to", "with", "why", "what", "when", "where"
    };

    private readonly OpenAiRagOptions _options = options.Value;

    public async Task<AuditChatResponse?> ChatAsync(Guid auditId, AuditChatRequest request, CancellationToken cancellationToken)
    {
        var message = request.EffectiveMessage;
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var record = await repository.GetAsync(auditId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var chunks = await llmChunker.TryChunkAsync(record.Request.Payload, cancellationToken) ??
            chunker.Chunk(record.Request.Payload);

        var citations = await RetrieveScopedCitationsAsync(message, chunks, cancellationToken);
        var suggestions = BuildSuggestions(record.Result);
        var answer = await TryAnswerWithLlmAsync(record, request, citations, cancellationToken);

        if (!string.IsNullOrWhiteSpace(answer))
        {
            return new AuditChatResponse(answer.Trim(), true, citations, suggestions);
        }

        return BuildFallbackResponse(record, message, citations, suggestions);
    }

    private async Task<IReadOnlyList<AuditChatCitation>> RetrieveScopedCitationsAsync(
        string question,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        if (embeddingService.IsConfigured)
        {
            try
            {
                var inputs = new[] { question }.Concat(chunks.Select(chunk => chunk.Text)).ToArray();
                var vectors = await embeddingService.EmbedAsync(inputs, cancellationToken);
                var questionVector = vectors[0];

                return chunks
                    .Zip(vectors.Skip(1), (chunk, vector) => ToCitation(chunk, CosineSimilarity(questionVector, vector)))
                    .OrderByDescending(citation => citation.Similarity)
                    .Take(5)
                    .ToArray();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Document chat embedding retrieval failed. Falling back to local lexical retrieval.");
            }
        }

        var queryTokens = Tokenize(question).ToArray();
        return chunks
            .Select(chunk => ToCitation(chunk, LexicalSimilarity(queryTokens, Tokenize(chunk.Text))))
            .OrderByDescending(citation => citation.Similarity)
            .Take(5)
            .ToArray();
    }

    private async Task<string?> TryAnswerWithLlmAsync(
        AuditRecord record,
        AuditChatRequest request,
        IReadOnlyList<AuditChatCitation> citations,
        CancellationToken cancellationToken)
    {
        var apiKey = LlmProviderConfiguration.GetApiKey(_options, configuration);
        if (string.IsNullOrWhiteSpace(apiKey) || citations.Count == 0)
        {
            return null;
        }

        var context = BuildContext(record, citations);
        if (context.Length > _options.MaxReasoningCharacters)
        {
            context = context[.._options.MaxReasoningCharacters];
        }

        var input = new List<object>
        {
            new
            {
                role = "system",
                content = """
You are the designated auditor for this specific document.
Answer only from the appended document excerpts and audit compliance logs.
If the answer cannot be found in that context, say that the provided document context does not contain enough information.
Do not use outside knowledge. Keep the answer concise, practical, and evidence-backed.
When referring to evidence, mention the cited excerpt or sentence number, not internal chunking terminology.
"""
            }
        };

        foreach (var message in request.History?.TakeLast(8) ?? [])
        {
            var role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
            input.Add(new { role, content = message.Content });
        }

        input.Add(new
        {
            role = "user",
            content = JsonSerializer.Serialize(new
            {
                question = request.EffectiveMessage,
                boundedContext = context
            }, SerializerOptions)
        });

        var requestBody = new
        {
            model = _options.ReasoningModel,
            input
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
                logger.LogWarning("Document chat completion failed with status {StatusCode}: {Response}", (int)response.StatusCode, responseBody);
                return null;
            }

            return ExtractResponseText(responseBody);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Document chat completion failed. Falling back to extractive response.");
            return null;
        }
    }

    private static AuditChatResponse BuildFallbackResponse(
        AuditRecord record,
        string question,
        IReadOnlyList<AuditChatCitation> citations,
        IReadOnlyList<string> suggestions)
    {
        if (IsDocumentReviewRequest(question))
        {
            return BuildDocumentReviewResponse(record, citations, suggestions);
        }

        var relevantCitations = citations.Where(citation => citation.Similarity > 0).Take(3).ToArray();
        var matchingViolations = record.Result?.Violations
            .Where(violation =>
                ContainsAnyToken(violation.Clause, question) ||
                ContainsAnyToken(violation.Reason, question))
            .Take(3)
            .ToArray() ?? [];

        if (relevantCitations.Length == 0 && matchingViolations.Length == 0)
        {
            return new AuditChatResponse(
                "The provided document context does not contain enough information to answer that question.",
                false,
                citations.Take(3).ToArray(),
                suggestions);
        }

        var builder = new StringBuilder();
        if (matchingViolations.Length > 0)
        {
            builder.AppendLine("Relevant audit findings:");
            foreach (var violation in matchingViolations)
            {
                builder.AppendLine($"- {violation.Clause} ({violation.Severity}): {violation.Reason}");
            }
        }

        if (relevantCitations.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("Relevant document sentences:");
            foreach (var citation in relevantCitations)
            {
                builder.AppendLine($"- Sentence {citation.ChunkIndex + 1}: {TrimExcerpt(citation.Text, 320)}");
            }
        }

        return new AuditChatResponse(builder.ToString().Trim(), true, relevantCitations, suggestions);
    }

    private static AuditChatResponse BuildDocumentReviewResponse(
        AuditRecord record,
        IReadOnlyList<AuditChatCitation> citations,
        IReadOnlyList<string> suggestions)
    {
        var payload = NormalizeWhitespace(record.Request.Payload);
        var documentName = ExtractDocumentName(payload);
        var period = ExtractPeriod(payload);
        var auditor = ExtractAuditor(payload);
        var opinion = ExtractAuditOpinion(payload);
        var financialSnapshot = ExtractFinancialSnapshot(payload);
        var organization = ExtractOrganization(payload);

        var builder = new StringBuilder();
        builder.AppendLine($"Document review: {documentName}");
        builder.AppendLine();
        builder.AppendLine($"This uploaded document appears to be {DescribeDocumentType(payload)}{ForOrganization(organization)}{ForPeriod(period)}.");

        if (!string.IsNullOrWhiteSpace(opinion))
        {
            builder.AppendLine($"The independent auditor's opinion is: {opinion}");
        }

        if (!string.IsNullOrWhiteSpace(auditor))
        {
            builder.AppendLine($"Auditor: {auditor.TrimEnd('.')}.");
        }

        if (financialSnapshot.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Key figures found in the document:");
            foreach (var item in financialSnapshot)
            {
                builder.AppendLine($"- {item}");
            }
        }

        if (record.Result is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"AuditWise status: {record.Result.ComplianceStatus} with risk score {record.Result.RiskScore:0.#}.");
            builder.AppendLine(record.Result.Violations.Count == 0
                ? "No configured compliance violations were detected from the current policy set."
                : "Detected findings should be reviewed before approval.");
        }

        return new AuditChatResponse(
            builder.ToString().Trim(),
            true,
            citations.Where(citation => citation.Similarity > 0).Take(3).ToArray(),
            suggestions);
    }

    private static string BuildContext(AuditRecord record, IReadOnlyList<AuditChatCitation> citations)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"DocumentId: {record.Id}");
        builder.AppendLine($"AuditStatus: {record.Status}");

        if (record.Result is not null)
        {
            builder.AppendLine("ComplianceLog:");
            builder.AppendLine(JsonSerializer.Serialize(new
            {
                record.Result.ComplianceStatus,
                record.Result.RiskScore,
                record.Result.DocumentIntent,
                record.Result.SuggestedRemediation,
                record.Result.Violations,
                record.Result.MatchedPolicies
            }, SerializerOptions));
            builder.AppendLine();
        }

        builder.AppendLine("DocumentExcerpts:");
        foreach (var citation in citations)
        {
            builder.AppendLine($"[Excerpt {citation.ChunkIndex + 1}; similarity {citation.Similarity:F3}]");
            builder.AppendLine(citation.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildSuggestions(AuditResult? result)
    {
        if (result?.Violations.Count > 0)
        {
            return result.Violations
                .Take(4)
                .Select(violation => $"Explain why {violation.Clause} was flagged.")
                .Concat([
                    "Summarize all compliance risks in this document.",
                    "Draft safer replacement language for the highest-risk clause."
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
        }

        return
        [
            "Summarize the compliance posture of this document.",
            "List the policy evidence found in this file.",
            "Identify clauses that may need legal review."
        ];
    }

    private static AuditChatCitation ToCitation(DocumentChunk chunk, double similarity) =>
        new(chunk.Index, chunk.Text, Math.Round(Math.Clamp(similarity, 0, 1), 3));

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        var dotProduct = 0d;
        var leftMagnitude = 0d;
        var rightMagnitude = 0d;

        for (var index = 0; index < length; index++)
        {
            dotProduct += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        return leftMagnitude == 0 || rightMagnitude == 0
            ? 0
            : dotProduct / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static double LexicalSimilarity(IReadOnlyList<string> queryTokens, IEnumerable<string> chunkTokens)
    {
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var chunkSet = chunkTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = queryTokens.Count(chunkSet.Contains);
        return (double)matches / queryTokens.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match match in TokenPattern.Matches(text.ToLowerInvariant()))
        {
            var token = match.Value;
            if (token.Length >= 3 && !StopWords.Contains(token))
            {
                yield return token;
            }
        }
    }

    private static bool ContainsAnyToken(string text, string query)
    {
        var tokens = Tokenize(query).ToArray();
        return tokens.Length > 0 && tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDocumentReviewRequest(string question)
    {
        var normalized = question.ToLowerInvariant();
        return normalized.Contains("document review") ||
               normalized.Contains("understandable") ||
               normalized.Contains("summarize") ||
               normalized.Contains("summary") ||
               normalized.Contains("uploaded document") ||
               normalized.Contains("based on the uploaded");
    }

    private static string NormalizeWhitespace(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim();

    private static string ExtractDocumentName(string text)
    {
        if (text.Contains("Permanent Secretariat", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("Transport Community", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("Financial Statements", StringComparison.OrdinalIgnoreCase))
        {
            var period = ExtractPeriod(text);
            return string.IsNullOrWhiteSpace(period)
                ? "Permanent Secretariat of the Transport Community financial statements"
                : $"Permanent Secretariat of the Transport Community financial statements for the year ended {period}";
        }

        var match = Regex.Match(text, @"(?<name>[A-Z][A-Z\s]{8,}Financial Statements\s*Year Ended\s*[^.]{8,40})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return NormalizeTitle(match.Groups["name"].Value);
        }

        if (text.Contains("financial statements", StringComparison.OrdinalIgnoreCase))
        {
            return "Financial statements and independent auditor's report";
        }

        return "Uploaded document";
    }

    private static string ExtractPeriod(string text)
    {
        var match = Regex.Match(text, @"Year Ended\s+(?<period>[A-Za-z]+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["period"].Value;
        }

        match = Regex.Match(text, @"as at\s+(?<period>\d{1,2}\s+[A-Za-z]+\s+\d{4})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["period"].Value : string.Empty;
    }

    private static string ExtractAuditor(string text)
    {
        if (text.Contains("Crowe RS Advisory", StringComparison.OrdinalIgnoreCase))
        {
            return "Crowe RS Advisory d.o.o.";
        }

        var match = Regex.Match(text, @"(?<auditor>[A-Z][A-Za-z\s&.]+)\s+INDEPENDENT AUDITOR", RegexOptions.IgnoreCase);
        return match.Success ? NormalizeTitle(match.Groups["auditor"].Value) : string.Empty;
    }

    private static string ExtractAuditOpinion(string text)
    {
        var match = Regex.Match(
            text,
            @"In our opinion,?\s+(?<opinion>.+?)(?=Other matter|Basis for Opinion|Responsibilities of Management|$)",
            RegexOptions.IgnoreCase);

        return match.Success ? TrimExcerpt(NormalizeWhitespace(match.Groups["opinion"].Value), 420) : string.Empty;
    }

    private static IReadOnlyList<string> ExtractFinancialSnapshot(string text)
    {
        var items = new List<string>();
        AddAmount(items, text, "total assets", "Total assets");
        AddAmount(items, text, "total current assets", "Total current assets");
        AddAmount(items, text, "cash and cash equivalents", "Cash and cash equivalents");
        AddAmount(items, text, "total revenue", "Total revenue");
        AddAmount(items, text, "total expenses", "Total expenses");
        AddAmount(items, text, "net surplus", "Net surplus for the period");
        AddAmount(items, text, "total unused budget appropriations", "Unused budget appropriations");

        return items.Distinct(StringComparer.OrdinalIgnoreCase).Take(7).ToArray();
    }

    private static void AddAmount(List<string> items, string text, string labelSearch, string label)
    {
        var index = text.IndexOf(labelSearch, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return;
        }

        var window = text.Substring(index, Math.Min(220, text.Length - index));
        var match = Regex.Match(window, @"(?<amount>\d{1,3}(?:,\d{3})+(?:\.\d{2})?|\d+\.\d{2})");
        if (match.Success)
        {
            items.Add($"{label}: EUR {match.Groups["amount"].Value}");
        }
    }

    private static string ExtractOrganization(string text)
    {
        if (text.Contains("Permanent Secretariat of the Transport Community", StringComparison.OrdinalIgnoreCase))
        {
            return "the Permanent Secretariat of the Transport Community";
        }

        var match = Regex.Match(text, @"financial statements of the (?<org>.+?) which comprise", RegexOptions.IgnoreCase);
        return match.Success ? NormalizeWhitespace(match.Groups["org"].Value) : string.Empty;
    }

    private static string DescribeDocumentType(string text)
    {
        if (text.Contains("independent auditor", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("financial statements", StringComparison.OrdinalIgnoreCase))
        {
            return "an independent auditor's report and annual financial statements";
        }

        if (text.Contains("contract", StringComparison.OrdinalIgnoreCase))
        {
            return "a contract or legal agreement";
        }

        return "a compliance document";
    }

    private static string ForOrganization(string organization) =>
        string.IsNullOrWhiteSpace(organization) ? string.Empty : $" for {organization}";

    private static string ForPeriod(string period) =>
        string.IsNullOrWhiteSpace(period) ? string.Empty : $" for the period ended {period}";

    private static string NormalizeTitle(string text)
    {
        var normalized = NormalizeWhitespace(text);
        return normalized.Length <= 1
            ? normalized
            : char.ToUpperInvariant(normalized[0]) + normalized[1..].ToLowerInvariant();
    }

    private static string TrimExcerpt(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength].TrimEnd() + "...";
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
}
