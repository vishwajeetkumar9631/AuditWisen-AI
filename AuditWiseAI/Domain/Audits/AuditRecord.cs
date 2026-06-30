namespace AuditWiseAI.Models;

public sealed record AuditRecord(
    Guid Id,
    AuditStatus Status,
    AuditRequest Request,
    AuditResult? Result,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
