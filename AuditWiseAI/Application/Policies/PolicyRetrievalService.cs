using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AuditWiseAI.Models;

namespace AuditWiseAI.Services;

public interface IPolicyRetrievalService
{
    Task<IReadOnlyList<PolicyReference>> SearchAsync(string documentIntent, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);
}

public interface IPolicyCatalog
{
    Task<IReadOnlyList<PolicyReference>> GetAllAsync(CancellationToken cancellationToken);
}

public sealed class LiteDbPolicyCatalog(AuditDatabase database) : IPolicyCatalog
{
    public Task<IReadOnlyList<PolicyReference>> GetAllAsync(CancellationToken cancellationToken)
    {
        var policies = database.Policies
            .FindAll()
            .OrderBy(policy => policy.PolicyId)
            .Select(policy => new PolicyReference(policy.PolicyId, policy.Title, policy.Text, 1))
            .ToArray();

        return Task.FromResult<IReadOnlyList<PolicyReference>>(policies);
    }
}

public sealed partial class DatabasePolicyRetrievalService(
    IPolicyCatalog policyCatalog,
    ITextEmbeddingService embeddingService,
    PolicyEmbeddingCache embeddingCache,
    ILogger<DatabasePolicyRetrievalService> logger) : IPolicyRetrievalService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "in", "is", "it",
        "must", "of", "on", "or", "that", "the", "this", "to", "with"
    };

    public async Task<IReadOnlyList<PolicyReference>> SearchAsync(string documentIntent, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        var policies = await policyCatalog.GetAllAsync(cancellationToken);
        if (policies.Count == 0 || chunks.Count == 0)
        {
            return [];
        }

        if (embeddingService.IsConfigured)
        {
            try
            {
                return await SearchWithEmbeddingsAsync(documentIntent, chunks, policies, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Embedding retrieval failed. Falling back to local TF-IDF retrieval.");
            }
        }

        var index = LocalPolicyRetrievalIndex.Build(policies);
        var scored = index.Search(documentIntent, chunks)
            .OrderByDescending(policy => policy.Similarity)
            .Take(3)
            .ToArray();

        return scored;
    }

    private async Task<IReadOnlyList<PolicyReference>> SearchWithEmbeddingsAsync(
        string documentIntent,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<PolicyReference> policies,
        CancellationToken cancellationToken)
    {
        var chunkEmbeddings = await embeddingService.EmbedAsync(
            chunks.Select(chunk => chunk.Text).ToArray(),
            cancellationToken);

        var policyVectors = await GetPolicyVectorsAsync(policies, cancellationToken);

        return policyVectors
            .Select(policy =>
            {
                var semanticScore = chunkEmbeddings.Max(chunk => CosineSimilarity(chunk, policy.Vector));
                var intentBoost = CalculateIntentBoost(documentIntent, InferCategory(policy.Policy));
                var score = Math.Clamp(semanticScore + intentBoost, 0, 0.99);
                return policy.Policy with { Similarity = Math.Round(score, 3) };
            })
            .OrderByDescending(policy => policy.Similarity)
            .Take(3)
            .ToArray();
    }

    private async Task<IReadOnlyList<PolicyVector>> GetPolicyVectorsAsync(
        IReadOnlyList<PolicyReference> policies,
        CancellationToken cancellationToken)
    {
        var vectors = new PolicyVector[policies.Count];
        var missingPolicies = new List<(int Index, PolicyReference Policy, string Key)>();

        for (var index = 0; index < policies.Count; index++)
        {
            var policy = policies[index];
            var key = GetPolicyEmbeddingCacheKey(policy);
            if (embeddingCache.TryGet(key, out var vector))
            {
                vectors[index] = new PolicyVector(policy, vector);
            }
            else
            {
                missingPolicies.Add((index, policy, key));
            }
        }

        if (missingPolicies.Count > 0)
        {
            var embeddings = await embeddingService.EmbedAsync(
                missingPolicies.Select(item => $"{item.Policy.Title}\n{item.Policy.Text}").ToArray(),
                cancellationToken);

            for (var index = 0; index < missingPolicies.Count; index++)
            {
                var missing = missingPolicies[index];
                var vector = embeddings[index];
                embeddingCache.Set(missing.Key, vector);
                vectors[missing.Index] = new PolicyVector(missing.Policy, vector);
            }
        }

        return vectors;
    }

    private static string GetPolicyEmbeddingCacheKey(PolicyReference policy) =>
        $"{policy.Id}:{policy.Title}:{policy.Text}".GetHashCode(StringComparison.Ordinal).ToString();

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        var dotProduct = 0d;
        var leftMagnitude = 0d;
        var rightMagnitude = 0d;

        for (var index = 0; index < length; index++)
        {
            dotProduct += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        return leftMagnitude == 0 || rightMagnitude == 0
            ? 0
            : dotProduct / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static double CalculateIntentBoost(string documentIntent, string category)
    {
        var intent = documentIntent.ToLowerInvariant();

        if (intent.Contains("privacy") && category == "privacy")
        {
            return 0.08;
        }

        if (intent.Contains("financial") && category == "finance")
        {
            return 0.08;
        }

        if (intent.Contains("diff") && category == "change-control")
        {
            return 0.08;
        }

        if (intent.Contains("security") && category == "security")
        {
            return 0.08;
        }

        return 0;
    }

    private sealed class LocalPolicyRetrievalIndex
    {
        private readonly IReadOnlyList<IndexedPolicy> _policies;
        private readonly IReadOnlyDictionary<string, double> _idf;

        private LocalPolicyRetrievalIndex(IReadOnlyList<IndexedPolicy> policies, IReadOnlyDictionary<string, double> idf)
        {
            _policies = policies;
            _idf = idf;
        }

        public static LocalPolicyRetrievalIndex Build(IReadOnlyList<PolicyReference> policies)
        {
            var tokenizedPolicies = policies
                .Select(policy =>
                {
                    var searchableText = $"{policy.Title} {policy.Text}";
                    var tokens = Tokenize(searchableText).ToArray();
                    return new
                    {
                        Policy = policy,
                        Category = InferCategory(policy),
                        Tokens = tokens
                    };
                })
                .ToArray();

            var documentFrequency = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var policy in tokenizedPolicies)
            {
                foreach (var token in policy.Tokens.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    documentFrequency.AddOrUpdate(token, 1, (_, count) => count + 1);
                }
            }

            var policyCount = Math.Max(tokenizedPolicies.Length, 1);
            var idf = documentFrequency.ToDictionary(
                pair => pair.Key,
                pair => Math.Log((policyCount + 1d) / (pair.Value + 0.5d)) + 1d,
                StringComparer.OrdinalIgnoreCase);

            var indexed = tokenizedPolicies
                .Select(policy => new IndexedPolicy(
                    policy.Policy,
                    policy.Category,
                    ToWeightedVector(policy.Tokens, idf)))
                .ToArray();

            return new LocalPolicyRetrievalIndex(indexed, idf);
        }

        public IReadOnlyList<PolicyReference> Search(string documentIntent, IReadOnlyList<DocumentChunk> chunks)
        {
            var queryVectors = chunks
                .Select(chunk => ToWeightedVector(Tokenize(chunk.Text), _idf))
                .Where(vector => vector.Count > 0)
                .ToArray();

            if (queryVectors.Length == 0)
            {
                return [];
            }

            return _policies
                .Select(policy =>
                {
                    var maxChunkSimilarity = queryVectors.Max(query => CosineSimilarity(query, policy.Vector));
                    var intentBoost = CalculateIntentBoost(documentIntent, policy);
                    var score = Math.Clamp(maxChunkSimilarity + intentBoost, 0, 0.99);
                    return policy.Policy with { Similarity = Math.Round(score, 3) };
                })
                .Where(policy => policy.Similarity > 0.05)
                .ToArray();
        }

        private static IReadOnlyDictionary<string, double> ToWeightedVector(
            IEnumerable<string> tokens,
            IReadOnlyDictionary<string, double> idf)
        {
            var counts = tokens
                .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var vector = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (token, count) in counts)
            {
                var inverseFrequency = idf.GetValueOrDefault(token, 1d);
                vector[token] = (1d + Math.Log(count)) * inverseFrequency;
            }

            return vector;
        }

        private static double CosineSimilarity(IReadOnlyDictionary<string, double> left, IReadOnlyDictionary<string, double> right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return 0;
            }

            var dotProduct = 0d;
            foreach (var (token, leftWeight) in left)
            {
                if (right.TryGetValue(token, out var rightWeight))
                {
                    dotProduct += leftWeight * rightWeight;
                }
            }

            if (dotProduct == 0)
            {
                return 0;
            }

            var leftMagnitude = Math.Sqrt(left.Values.Sum(weight => weight * weight));
            var rightMagnitude = Math.Sqrt(right.Values.Sum(weight => weight * weight));
            return leftMagnitude == 0 || rightMagnitude == 0 ? 0 : dotProduct / (leftMagnitude * rightMagnitude);
        }

        private static double CalculateIntentBoost(string documentIntent, IndexedPolicy policy)
        {
            var intent = documentIntent.ToLowerInvariant();
            var category = policy.Category;

            if (intent.Contains("privacy") && category == "privacy")
            {
                return 0.18;
            }

            if (intent.Contains("financial") && category == "finance")
            {
                return 0.18;
            }

            if (intent.Contains("diff") && category == "change-control")
            {
                return 0.18;
            }

            if (intent.Contains("security") && category == "security")
            {
                return 0.18;
            }

            return 0;
        }

        private static string InferCategory(PolicyReference policy)
        {
            return DatabasePolicyRetrievalService.InferCategory(policy);
        }

        private sealed record IndexedPolicy(
            PolicyReference Policy,
            string Category,
            IReadOnlyDictionary<string, double> Vector);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match match in TokenPattern().Matches(text.ToLowerInvariant()))
        {
            var token = match.Value;
            if (token.Length >= 3 && !StopWords.Contains(token))
            {
                yield return token;
            }
        }
    }

    [GeneratedRegex("[a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex TokenPattern();

    private static string InferCategory(PolicyReference policy)
    {
        var text = $"{policy.Id} {policy.Title} {policy.Text}".ToLowerInvariant();

        if (text.Contains("gdpr") || text.Contains("privacy") || text.Contains("retention") || text.Contains("personal data"))
        {
            return "privacy";
        }

        if (text.Contains("soc2") || text.Contains("change") || text.Contains("pull request") || text.Contains("rollback"))
        {
            return "change-control";
        }

        if (text.Contains("vendor") || text.Contains("contract") || text.Contains("liability") || text.Contains("finance"))
        {
            return "finance";
        }

        if (text.Contains("security") || text.Contains("encrypted") || text.Contains("tenant"))
        {
            return "security";
        }

        return "general";
    }

    private sealed record PolicyVector(PolicyReference Policy, float[] Vector);
}
