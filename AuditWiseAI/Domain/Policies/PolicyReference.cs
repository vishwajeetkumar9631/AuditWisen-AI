namespace AuditWiseAI.Models;

public sealed record PolicyReference(
    string Id,
    string Title,
    string Text,
    double Similarity);
