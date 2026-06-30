namespace AuditWiseAI.Models;

public sealed record AuditResult(
    ComplianceStatus ComplianceStatus,
    double RiskScore,
    IReadOnlyList<ComplianceViolation> Violations,
    string SuggestedRemediation,
    string DocumentIntent,
    IReadOnlyList<PolicyReference> MatchedPolicies,
    DateTimeOffset CompletedAt);
