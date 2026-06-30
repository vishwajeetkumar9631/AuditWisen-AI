namespace AuditWiseAI.Models;

public sealed record ComplianceViolation(
    string Clause,
    ViolationSeverity Severity,
    string Reason);
