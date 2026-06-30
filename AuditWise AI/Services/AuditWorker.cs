using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public sealed class AuditWorker(
    IAuditJobQueue queue,
    IAuditRepository repository,
    IComplianceAnalysisService analysisService,
    IWebhookDispatcher webhookDispatcher,
    IAuditRealtimeNotifier realtimeNotifier,
    ILogger<AuditWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var job = await queue.DequeueAsync(stoppingToken);
                await ProcessAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Audit worker is stopping.");
        }
    }

    private async Task ProcessAsync(QueuedAudit job, CancellationToken cancellationToken)
    {
        try
        {
            var record = await repository.GetAsync(job.AuditId, cancellationToken);
            if (record is null)
            {
                logger.LogWarning("Audit {AuditId} was queued but no record exists.", job.AuditId);
                return;
            }

            await repository.MarkProcessingAsync(job.AuditId, cancellationToken);
            var processing = await repository.GetAsync(job.AuditId, cancellationToken);
            if (processing is not null)
            {
                await realtimeNotifier.PublishAsync(processing, cancellationToken);
            }

            var result = await analysisService.AnalyzeAsync(record.Request, cancellationToken);
            await repository.CompleteAsync(job.AuditId, result, cancellationToken);

            var completed = await repository.GetAsync(job.AuditId, cancellationToken);
            if (completed is not null)
            {
                await realtimeNotifier.PublishAsync(completed, cancellationToken);
                await DispatchWebhookAsync(completed, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Audit {AuditId} processing was canceled during shutdown.", job.AuditId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Audit {AuditId} failed.", job.AuditId);
            await repository.FailAsync(job.AuditId, exception.Message, cancellationToken);
            var failed = await repository.GetAsync(job.AuditId, cancellationToken);
            if (failed is not null)
            {
                await realtimeNotifier.PublishAsync(failed, cancellationToken);
            }
        }
    }

    private async Task DispatchWebhookAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await webhookDispatcher.DispatchAsync(record, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Audit {AuditId} webhook dispatch was canceled during shutdown.", record.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Audit {AuditId} completed, but webhook dispatch to {CallbackUrl} failed.",
                record.Id,
                record.Request.CallbackUrl);
        }
    }
}
