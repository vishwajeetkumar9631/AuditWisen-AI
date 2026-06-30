using System.Threading.Channels;
using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public interface IAuditJobQueue
{
    ValueTask EnqueueAsync(QueuedAudit audit, CancellationToken cancellationToken);
    ValueTask<QueuedAudit> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class AuditJobQueue : IAuditJobQueue
{
    private readonly Channel<QueuedAudit> _channel = Channel.CreateUnbounded<QueuedAudit>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(QueuedAudit audit, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(audit, cancellationToken);

    public ValueTask<QueuedAudit> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
