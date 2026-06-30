using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public interface IWebhookDispatcher
{
    Task DispatchAsync(AuditRecord record, CancellationToken cancellationToken);
}

public sealed class LoggingWebhookDispatcher(
    ILogger<LoggingWebhookDispatcher> logger,
    IHttpClientFactory httpClientFactory) : IWebhookDispatcher
{
    public async Task DispatchAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        if (record.Request.CallbackUrl is null)
        {
            logger.LogInformation("Audit {AuditId} completed with no callback URL.", record.Id);
            return;
        }

        using var client = httpClientFactory.CreateClient("webhooks");
        var response = await client.PostAsJsonAsync(record.Request.CallbackUrl, record, cancellationToken);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Audit {AuditId} dispatched to {CallbackUrl}.", record.Id, record.Request.CallbackUrl);
    }
}
