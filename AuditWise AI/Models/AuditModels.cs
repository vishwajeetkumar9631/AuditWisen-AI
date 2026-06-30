using System.Text.Json.Serialization;

namespace AuditWiseAI.Models;

public enum AuditStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public enum ComplianceStatus
{
    Passed,
    Flagged,
    NeedsReview
}

public enum ViolationSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public sealed record AuditRequest(
    string Payload,
    string ContentType = "text/plain",
    string? SourceSystem = null,
    string? CorrelationId = null,
    Uri? CallbackUrl = null);

public sealed record AuditAcceptedResponse(
    Guid AuditId,
    AuditStatus Status,
    string StatusUrl,
    DateTimeOffset AcceptedAt);

public sealed record AuditRecord(
    Guid Id,
    AuditStatus Status,
    AuditRequest Request,
    AuditResult? Result,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AuditResult(
    ComplianceStatus ComplianceStatus,
    double RiskScore,
    IReadOnlyList<ComplianceViolation> Violations,
    string SuggestedRemediation,
    string DocumentIntent,
    IReadOnlyList<PolicyReference> MatchedPolicies,
    DateTimeOffset CompletedAt);

public sealed record ComplianceViolation(
    string Clause,
    ViolationSeverity Severity,
    string Reason);

public sealed record PolicyReference(
    string Id,
    string Title,
    string Text,
    double Similarity);

public sealed record DocumentChunk(
    int Index,
    string Text,
    int ApproximateTokenCount);

public sealed record AuditChatRequest(
    string? Message = null,
    IReadOnlyList<AuditChatMessage>? History = null,
    string? Prompt = null,
    string? Question = null,
    string? Input = null)
{
    [JsonIgnore]
    public string EffectiveMessage =>
        FirstNonEmpty(Message, Prompt, Question, Input) ?? string.Empty;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed record AuditChatMessage(
    string Role,
    string Content);

public sealed record AuditChatResponse(
    string Message,
    bool AnsweredFromContext,
    IReadOnlyList<AuditChatCitation> Citations,
    IReadOnlyList<string> Suggestions);

public sealed record AuditChatCitation(
    int ChunkIndex,
    string Text,
    double Similarity);

public sealed record QueuedAudit(Guid AuditId);
