namespace AuditWiseAI.Models;

public sealed record DocumentChunk(
    int Index,
    string Text,
    int ApproximateTokenCount);
