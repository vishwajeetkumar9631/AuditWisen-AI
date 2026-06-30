using System.Security.Cryptography;
using System.Text;
using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public interface ISemanticCache
{
    Task<AuditResult?> TryGetAsync(string payload, CancellationToken cancellationToken);
    Task SetAsync(string payload, AuditResult result, CancellationToken cancellationToken);
}

public sealed class InMemorySemanticCache : ISemanticCache
{
    private readonly Dictionary<string, AuditResult> _cache = new();
    private readonly object _gate = new();

    public Task<AuditResult?> TryGetAsync(string payload, CancellationToken cancellationToken)
    {
        var key = ComputeKey(payload);
        lock (_gate)
        {
            return Task.FromResult(_cache.GetValueOrDefault(key));
        }
    }

    public Task SetAsync(string payload, AuditResult result, CancellationToken cancellationToken)
    {
        var key = ComputeKey(payload);
        lock (_gate)
        {
            _cache[key] = result;
        }

        return Task.CompletedTask;
    }

    private static string ComputeKey(string payload)
    {
        var normalized = string.Join(' ', payload.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }
}
