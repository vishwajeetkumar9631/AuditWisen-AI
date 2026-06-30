# AuditWise AI

AuditWise AI is a .NET Minimal API service for compliance audit analysis. It accepts text or uploaded documents, chunks large content, retrieves relevant policy context, produces structured risk results, and processes asynchronous audit jobs through a background worker.

## Project Name

GitHub repository name: `AuditWisen-AI`

Display name: `AuditWise AI`

## Features

- Compliance audit API with asynchronous and synchronous analysis endpoints.
- LiteDB-backed local persistence for audits and seeded policies.
- Background worker and in-memory queue for audit processing.
- Policy retrieval with local TF-IDF fallback and optional OpenAI embeddings.
- Groq/OpenAI-compatible LLM reasoning support when API keys are configured.
- PDF/image/text upload support with OCR through Tesseract.
- SignalR audit updates and optional webhook dispatch.

## Requirements

- .NET SDK matching the project target framework in `AuditWise AI/AuditWise AI.csproj`.
- Optional: `GROQ_API_KEY` for Groq-backed reasoning.
- Optional: `OPENAI_API_KEY` for OpenAI embeddings/reasoning.

## Run Locally

From the repository root:

```powershell
dotnet restore "AuditWise AI/AuditWise AI.csproj"
dotnet run --project "AuditWise AI/AuditWise AI.csproj" --urls http://localhost:5055
```

Health check:

```powershell
Invoke-RestMethod http://localhost:5055/health
```

## API Endpoints

- `GET /health` - service health check.
- `GET /api/db/status` - local database status and record counts.
- `GET /api/policies` - seeded compliance policy catalog.
- `GET /api/audits?take=20` - recent audits.
- `POST /api/audits` - queue an asynchronous audit.
- `POST /api/audits/files` - queue an audit from an uploaded file.
- `POST /api/audits/analyze-sync` - run immediate analysis.
- `POST /api/audits/files/analyze-sync` - run immediate file analysis.
- `GET /api/audits/{id}` - audit status/result.
- `POST /api/audits/{id}/chat` - chat against an audit document.

## Example Request

```json
{
  "payload": "Vendor contract Section 4.2 data retention stores personal data for 10 years. Liability is uncapped.",
  "contentType": "text/plain",
  "sourceSystem": "demo"
}
```

## Notes

Local runtime data is stored under `AuditWise AI/App_Data/` and is intentionally ignored by Git. Do not commit API keys, local database files, build output, or IDE state.
