# AuditWise AI

AuditWise AI is a compliance audit platform with a .NET Minimal API backend and a Next.js frontend for audit upload and AI-assisted audit workflows.

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
- Next.js frontend for uploading audits and reviewing results.

## Architecture

```text
AuditWiseAI/
  Domain/           Core audit, document, and policy records.
  Models/           API request and response contracts.
  Application/      Audit workflows, document chunking, policy retrieval, and caching.
  Infrastructure/   LiteDB persistence, file/OCR handling, and LLM provider adapters.
  Integrations/     External callbacks such as webhooks.
  Realtime/         SignalR hub and audit update notifications.
  Program.cs        Minimal API composition root and endpoint mapping.
```

## Requirements

- .NET SDK matching the project target framework in `AuditWiseAI/AuditWiseAI.csproj`.
- Node.js and npm for the frontend.
- Optional: `GROQ_API_KEY` for Groq-backed reasoning.
- Optional: `OPENAI_API_KEY` for OpenAI embeddings/reasoning.

## Backend

From the repository root:

```powershell
dotnet restore AuditWiseAI/AuditWiseAI.csproj
dotnet run --project AuditWiseAI/AuditWiseAI.csproj --urls http://localhost:5055
```

Health check:

```powershell
Invoke-RestMethod http://localhost:5055/health
```

## Frontend

Install dependencies:

```bash
npm install
```

Run the development server:

```bash
npm run dev
```

Build for production:

```bash
npm run build
```

Run lint checks:

```bash
npm run lint
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

## Tech Stack

- .NET Minimal API
- Next.js
- React
- TypeScript
- Tailwind CSS

## Notes

Local runtime data is stored under `AuditWiseAI/App_Data/` and is intentionally ignored by Git. Do not commit API keys, local database files, build output, dependencies, or IDE state.
