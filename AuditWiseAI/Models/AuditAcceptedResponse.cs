namespace AuditWiseAI.Models;

public sealed record AuditAcceptedResponse(
    Guid AuditId,
    AuditStatus Status,
    string StatusUrl,
    DateTimeOffset AcceptedAt);
