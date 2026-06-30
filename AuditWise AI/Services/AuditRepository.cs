using System.Text.Json;
using System.Text.Json.Serialization;
using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public interface IAuditRepository
{
    Task<AuditRecord> CreateAsync(AuditRequest request, CancellationToken cancellationToken);
    Task<AuditRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditRecord>> ListAsync(int take, CancellationToken cancellationToken);
    Task MarkProcessingAsync(Guid id, CancellationToken cancellationToken);
    Task CompleteAsync(Guid id, AuditResult result, CancellationToken cancellationToken);
    Task FailAsync(Guid id, string error, CancellationToken cancellationToken);
}

public sealed class LiteDbAuditRepository(AuditDatabase database) : IAuditRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<AuditRecord> CreateAsync(AuditRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new AuditRecord(Guid.NewGuid(), AuditStatus.Queued, request, null, null, now, now);
        database.Audits.Insert(ToDocument(record));
        return Task.FromResult(record);
    }

    public Task<AuditRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = database.Audits.FindById(id);
        return Task.FromResult(document is null ? null : ToRecord(document));
    }

    public Task<IReadOnlyList<AuditRecord>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var records = database.Audits
            .FindAll()
            .OrderByDescending(audit => audit.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(ToRecord)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AuditRecord>>(records);
    }

    public Task MarkProcessingAsync(Guid id, CancellationToken cancellationToken)
    {
        Update(id, record => record with { Status = AuditStatus.Processing, UpdatedAt = DateTimeOffset.UtcNow });
        return Task.CompletedTask;
    }

    public Task CompleteAsync(Guid id, AuditResult result, CancellationToken cancellationToken)
    {
        Update(id, record => record with
        {
            Status = AuditStatus.Completed,
            Result = result,
            Error = null,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task FailAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        Update(id, record => record with
        {
            Status = AuditStatus.Failed,
            Error = error,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        return Task.CompletedTask;
    }

    private void Update(Guid id, Func<AuditRecord, AuditRecord> update)
    {
        var existing = database.Audits.FindById(id) ??
            throw new InvalidOperationException($"Audit '{id}' does not exist.");

        database.Audits.Update(ToDocument(update(ToRecord(existing))));
    }

    private static AuditDocument ToDocument(AuditRecord record)
    {
        return new AuditDocument
        {
            Id = record.Id,
            Status = record.Status.ToString(),
            RequestJson = JsonSerializer.Serialize(record.Request, SerializerOptions),
            ResultJson = record.Result is null ? null : JsonSerializer.Serialize(record.Result, SerializerOptions),
            Error = record.Error,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }

    private static AuditRecord ToRecord(AuditDocument document)
    {
        var request = JsonSerializer.Deserialize<AuditRequest>(document.RequestJson, SerializerOptions) ??
            throw new InvalidOperationException($"Audit '{document.Id}' has invalid request JSON.");

        var result = document.ResultJson is null
            ? null
            : JsonSerializer.Deserialize<AuditResult>(document.ResultJson, SerializerOptions);

        return new AuditRecord(
            document.Id,
            Enum.Parse<AuditStatus>(document.Status),
            request,
            result,
            document.Error,
            document.CreatedAt,
            document.UpdatedAt);
    }
}
