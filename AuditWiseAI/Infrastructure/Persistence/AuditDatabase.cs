using LiteDB;

namespace AuditWiseAI.Services;

public sealed class AuditDatabase : IDisposable
{
    private readonly LiteDatabase _database;

    public AuditDatabase(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);

        DatabasePath = Path.Combine(dataDirectory, "auditwise.db");
        _database = new LiteDatabase($"Filename={DatabasePath};Connection=shared");
    }

    public string DatabasePath { get; }

    public ILiteCollection<AuditDocument> Audits => _database.GetCollection<AuditDocument>("audits");

    public ILiteCollection<PolicyDocument> Policies => _database.GetCollection<PolicyDocument>("policies");

    public void Initialize()
    {
        Audits.EnsureIndex(audit => audit.Status);
        Audits.EnsureIndex(audit => audit.CreatedAt);
        Policies.EnsureIndex(policy => policy.PolicyId, unique: true);

        if (Policies.Count() > 0)
        {
            return;
        }

        Policies.InsertBulk(
        [
            new PolicyDocument
            {
                PolicyId = "GDPR-RETENTION-001",
                Title = "GDPR Data Retention",
                Text = "Personal data retention must not exceed 5 years unless a regulatory exception is documented.",
                Category = "privacy"
            },
            new PolicyDocument
            {
                PolicyId = "SEC-DATA-ISO-002",
                Title = "Data Isolation",
                Text = "Regulated data must be encrypted with AES-256 and isolated by tenant boundary.",
                Category = "security"
            },
            new PolicyDocument
            {
                PolicyId = "SOC2-CHANGE-003",
                Title = "SOC2 Change Control",
                Text = "Production pull requests require review evidence, test proof, and rollback notes.",
                Category = "change-control"
            },
            new PolicyDocument
            {
                PolicyId = "FIN-CONTRACT-004",
                Title = "Financial Exposure",
                Text = "Vendor contracts with uncapped liability or auto-renewal require finance and legal approval.",
                Category = "finance"
            }
        ]);
    }

    public void Dispose() => _database.Dispose();
}

public sealed class AuditDocument
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RequestJson { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PolicyDocument
{
    public int Id { get; set; }
    public string PolicyId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
