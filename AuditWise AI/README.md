# AuditWise AI

AuditWise AI is a .NET Minimal API prototype for an enterprise compliance audit engine. It accepts compliance payloads, chunks large documents, retrieves matching policy context, calculates a structured risk result, and processes asynchronous audit jobs through a background worker.

## Run

```powershell
dotnet run --project "AuditWise AI.csproj" --urls http://localhost:5055
```

To enable Groq-backed LLM chunking and audit reasoning, set a Groq API key before starting the API:

```powershell
$env:GROQ_API_KEY = "your-groq-api-key"
dotnet run --project "AuditWise AI.csproj" --urls http://localhost:5055
```

The default LLM provider is Groq using `llama-3.3-70b-versatile` at `https://api.groq.com/openai/v1`.
Embeddings still require OpenAI; when only Groq is configured, the app uses local TF-IDF retrieval instead of embedding-backed retrieval.
If no API key is configured, the app falls back to local sliding-window chunking, TF-IDF retrieval, and deterministic audit scoring.

## Endpoints

- `GET /health` - service health check.
- `GET /api/audits?take=20` - returns recent persisted audits.
- `POST /api/audits` - queues an asynchronous audit and returns a status URL.
- `GET /api/audits/{id}` - returns queued, processing, completed, or failed audit state.
- `POST /api/audits/analyze-sync` - runs immediate analysis for testing and demos.
- `GET /api/db/status` - returns local database file path and record counts.
- `GET /api/policies` - returns the seeded compliance policy catalog.

## Example Request

```json
{
  "payload": "Vendor contract Section 4.2 data retention stores personal data for 10 years. Liability is uncapped.",
  "contentType": "text/plain",
  "sourceSystem": "demo"
}
```

## Current Implementation

- LiteDB local database at `App_Data/auditwise.db`.
- Channel-backed queue that stands in for MassTransit/RabbitMQ or Azure Service Bus.
- Sliding-window document chunker.
- Groq-backed LLM chunking and audit reasoning when `GROQ_API_KEY` is configured.
- Optional OpenAI embeddings for policy/document retrieval when `OPENAI_API_KEY` is configured and the provider is set to OpenAI.
- Local fallback RAG over LiteDB policies using document chunking, TF-IDF weighting, cosine similarity, and intent-aware ranking.
- In-memory semantic cache keyed by normalized payload.
- Background worker for out-of-process style audit execution.
- Optional callback webhook dispatch.

## Production Extension Points

Replace these interfaces with production adapters when credentials and infrastructure are ready:

- `IPolicyRetrievalService` -> Supabase PGVector or Qdrant.
- `ISemanticCache` -> Redis OM semantic cache.
- `IAuditJobQueue` -> MassTransit transport.
- `IComplianceAnalysisService` -> Semantic Kernel agent orchestration with provider-backed LLM calls.
- `IWebhookDispatcher` -> GitHub, Jira, Slack, or internal workflow integrations.
