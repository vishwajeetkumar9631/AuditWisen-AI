using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public interface IDocumentChunker
{
    IReadOnlyList<DocumentChunk> Chunk(string document, int maxTokens = 240, int overlapTokens = 40);
}

public sealed class SlidingWindowDocumentChunker : IDocumentChunker
{
    public IReadOnlyList<DocumentChunk> Chunk(string document, int maxTokens = 240, int overlapTokens = 40)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document);

        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Max tokens must be greater than zero.");
        }

        if (overlapTokens < 0 || overlapTokens >= maxTokens)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapTokens), "Overlap must be between zero and max tokens.");
        }

        var tokens = document
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return [];
        }

        var chunks = new List<DocumentChunk>();
        var step = maxTokens - overlapTokens;
        var index = 0;

        for (var start = 0; start < tokens.Length; start += step)
        {
            var length = Math.Min(maxTokens, tokens.Length - start);
            var text = string.Join(' ', tokens.AsSpan(start, length).ToArray());
            chunks.Add(new DocumentChunk(index++, text, length));

            if (start + length >= tokens.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
