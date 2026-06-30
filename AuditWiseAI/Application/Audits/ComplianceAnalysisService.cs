using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public interface IComplianceAnalysisService
{
    Task<AuditResult> AnalyzeAsync(AuditRequest request, CancellationToken cancellationToken);
}

public sealed class ComplianceAnalysisService(
    IDocumentChunker chunker,
    ILlmDocumentChunker llmChunker,
    IPolicyRetrievalService policyRetrievalService,
    IAuditReasoningService auditReasoningService,
    ISemanticCache semanticCache) : IComplianceAnalysisService
{
    public async Task<AuditResult> AnalyzeAsync(AuditRequest request, CancellationToken cancellationToken)
    {
        var cached = await semanticCache.TryGetAsync(request.Payload, cancellationToken);
        if (cached is not null)
        {
            return cached with { CompletedAt = DateTimeOffset.UtcNow };
        }

        var intent = ClassifyIntent(request.Payload);
        var chunks = await llmChunker.TryChunkAsync(request.Payload, cancellationToken) ??
            chunker.Chunk(request.Payload);
        var policies = await policyRetrievalService.SearchAsync(intent, chunks, cancellationToken);
        var violations = DetectViolations(request.Payload, policies);
        var llmResult = await auditReasoningService.TryEvaluateAsync(
            request,
            intent,
            chunks,
            policies,
            violations,
            cancellationToken);

        if (llmResult is not null)
        {
            await semanticCache.SetAsync(request.Payload, llmResult, cancellationToken);
            return llmResult;
        }

        var riskScore = CalculateRiskScore(violations);
        var result = new AuditResult(
            riskScore >= 70 ? ComplianceStatus.Flagged : violations.Count > 0 ? ComplianceStatus.NeedsReview : ComplianceStatus.Passed,
            riskScore,
            violations,
            BuildRemediation(violations),
            intent,
            policies,
            DateTimeOffset.UtcNow);

        await semanticCache.SetAsync(request.Payload, result, cancellationToken);
        return result;
    }

    private static string ClassifyIntent(string payload)
    {
        var text = payload.ToLowerInvariant();

        if (text.Contains("diff --git") || text.Contains("pull request") || text.Contains("rollback"))
        {
            return "system diff";
        }

        if (text.Contains("vendor") || text.Contains("nda") || text.Contains("liability") || text.Contains("contract"))
        {
            return "financial contract";
        }

        if (text.Contains("retention") || text.Contains("gdpr") || text.Contains("personal data"))
        {
            return "privacy policy";
        }

        if (text.Contains("encrypted") || text.Contains("unencrypted") || text.Contains("plain text") || text.Contains("tenant"))
        {
            return "security control";
        }

        return "general compliance document";
    }

    private static List<ComplianceViolation> DetectViolations(string payload, IReadOnlyList<PolicyReference> policies)
    {
        var text = payload.ToLowerInvariant();
        var violations = new List<ComplianceViolation>();

        if ((text.Contains("10 years") || text.Contains("ten years")) &&
            policies.Any(policy => policy.Id == "GDPR-RETENTION-001"))
        {
            violations.Add(new ComplianceViolation(
                "Section 4.2 data retention",
                ViolationSeverity.Critical,
                "Document indicates data storage for 10 years, exceeding the configured 5-year retention control."));
        }

        if (text.Contains("uncapped liability") || text.Contains("liability is uncapped") || text.Contains("auto-renew"))
        {
            violations.Add(new ComplianceViolation(
                "Commercial liability terms",
                ViolationSeverity.High,
                "Financial exposure terms require explicit finance and legal approval."));
        }

        if ((text.Contains("production") || text.Contains("prod")) &&
            text.Contains("pull request") &&
            IsMissingRollbackEvidence(text))
        {
            violations.Add(new ComplianceViolation(
                "SOC2 change evidence",
                ViolationSeverity.Medium,
                "Production change request is missing rollback notes."));
        }

        if (text.Contains("unencrypted") || text.Contains("plain text"))
        {
            violations.Add(new ComplianceViolation(
                "Data protection",
                ViolationSeverity.Critical,
                "Regulated data appears to be stored or transferred without required encryption."));
        }

        return violations;
    }

    private static bool IsMissingRollbackEvidence(string text)
    {
        return !text.Contains("rollback") ||
               text.Contains("without rollback") ||
               text.Contains("no rollback") ||
               text.Contains("missing rollback");
    }

    private static double CalculateRiskScore(IReadOnlyList<ComplianceViolation> violations)
    {
        if (violations.Count == 0)
        {
            return 8.5;
        }

        var score = violations.Sum(violation => violation.Severity switch
        {
            ViolationSeverity.Critical => 81,
            ViolationSeverity.High => 34,
            ViolationSeverity.Medium => 16,
            _ => 8
        });

        return Math.Min(99, score + (violations.Count * 3.5));
    }

    private static string BuildRemediation(IReadOnlyList<ComplianceViolation> violations)
    {
        if (violations.Count == 0)
        {
            return "No remediation required. Retain evidence of this automated review.";
        }

        if (violations.Any(violation => violation.Clause.Contains("retention", StringComparison.OrdinalIgnoreCase)))
        {
            return "Amend retention period to a maximum of 5 years or add a documented regulatory exception clause.";
        }

        if (violations.Any(violation => violation.Clause.Contains("liability", StringComparison.OrdinalIgnoreCase)))
        {
            return "Route contract to finance and legal approvers, then cap liability and disable automatic renewal unless approved.";
        }

        return "Add missing compliance evidence, update the payload, and re-run the audit.";
    }
}
