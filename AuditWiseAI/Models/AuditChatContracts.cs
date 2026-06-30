using System.Text.Json.Serialization;

namespace AuditWiseAI.Models;

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
