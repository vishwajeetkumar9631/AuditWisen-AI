namespace AuditWiseAI.Models;

public sealed record AuditRequest(
    string Payload,
    string ContentType = "text/plain",
    string? SourceSystem = null,
    string? CorrelationId = null,
    Uri? CallbackUrl = null);
