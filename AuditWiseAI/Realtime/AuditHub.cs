using AuditWiseAI.Models;
using Microsoft.AspNetCore.SignalR;

namespace AuditWiseAI.Services;

public sealed class AuditHub : Hub
{
}

public interface IAuditRealtimeNotifier
{
    Task PublishAsync(AuditRecord record, CancellationToken cancellationToken);
}

public sealed class SignalRAuditRealtimeNotifier(IHubContext<AuditHub> hubContext) : IAuditRealtimeNotifier
{
    public Task PublishAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        var payload = new
        {
            id = record.Id.ToString(),
            title = record.Request.CorrelationId ?? record.Request.SourceSystem ?? $"Audit {record.Id:N}"[..14],
            owner = record.Request.SourceSystem ?? "Backend",
            status = ToFrontendStatus(record),
            riskScore = record.Result?.RiskScore ?? 0,
            policy = record.Result?.MatchedPolicies.FirstOrDefault()?.Title ?? "Policy retrieval",
            category = record.Result?.DocumentIntent ?? "Compliance",
            summary = record.Error ?? record.Result?.SuggestedRemediation ?? "Audit is processing.",
            message = record.Error ?? "Audit update received."
        };

        return hubContext.Clients.All.SendAsync("ReceiveAuditResult", payload, cancellationToken);
    }

    private static string ToFrontendStatus(AuditRecord record)
    {
        return record.Status switch
        {
            AuditStatus.Completed when record.Result?.ComplianceStatus == ComplianceStatus.Passed => "Passed",
            AuditStatus.Completed => "Flagged",
            AuditStatus.Failed => "Flagged",
            AuditStatus.Processing => "Analyzing Policies",
            _ => "Processing"
        };
    }
}
