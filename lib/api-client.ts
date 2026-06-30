export const API_URL = process.env.NEXT_PUBLIC_API_URL?.trim() || "http://localhost:5055";

export const auditTextPath = process.env.NEXT_PUBLIC_AUDIT_TEXT_PATH ?? "/api/audits";
export const auditFilePath = process.env.NEXT_PUBLIC_AUDIT_FILE_PATH ?? "/api/audits/files";
export const healthPath = process.env.NEXT_PUBLIC_HEALTH_PATH ?? "/health";

export function apiUrl(pathOrUrl: string) {
  if (!pathOrUrl) return "";
  if (pathOrUrl.startsWith("http://") || pathOrUrl.startsWith("https://")) return pathOrUrl;
  if (!API_URL) return pathOrUrl;

  const base = API_URL.replace(/\/$/, "");
  const path = pathOrUrl.startsWith("/") ? pathOrUrl : `/${pathOrUrl}`;
  return `${base}${path}`;
}

async function parseJsonResponse(response: Response) {
  const text = await response.text();
  const data = text ? tryParseJson(text) : null;

  if (!response.ok) {
    const message =
      typeof data?.message === "string"
        ? data.message
        : typeof data?.title === "string"
          ? data.title
          : `Request failed with ${response.status}`;
    throw new Error(message);
  }

  return data;
}

function tryParseJson(text: string) {
  try {
    return JSON.parse(text);
  } catch {
    return { message: text };
  }
}

async function requestJson(input: RequestInfo | URL, init?: RequestInit) {
  try {
    const response = await fetch(input, init);
    return parseJsonResponse(response);
  } catch (error) {
    if (error instanceof TypeError) {
      throw new Error(`Backend is not reachable at ${API_URL || "the configured API URL"}. Start the backend or update NEXT_PUBLIC_API_URL.`);
    }

    throw error;
  }
}

export async function probeBackend() {
  try {
    const response = await fetch(apiUrl(healthPath), { method: "GET" });
    if (!response.ok) {
      return { ok: false, message: `Backend health check failed with ${response.status}.` };
    }

    return { ok: true };
  } catch {
    return { ok: false, message: `No response from ${API_URL}.` };
  }
}

export async function submitAuditText(payload: string, title?: string) {
  return requestJson(apiUrl(auditTextPath), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      title,
      payload,
      contentType: "text/plain",
      sourceSystem: "frontend"
    })
  });
}

export async function submitAuditFile(file: File, title?: string) {
  const form = new FormData();
  form.append("file", file);
  if (title) form.append("title", title);
  form.append("sourceSystem", "frontend");

  return requestJson(apiUrl(auditFilePath), {
    method: "POST",
    body: form
  });
}

export async function fetchAuditStatus(statusUrl: string) {
  return requestJson(apiUrl(statusUrl));
}

export type BackendAuditRecord = {
  id: string;
  status: "Queued" | "Processing" | "Completed" | "Failed";
  request: {
    payload: string;
    contentType: string;
    sourceSystem?: string | null;
    correlationId?: string | null;
  };
  result?: {
    complianceStatus: "Passed" | "Flagged" | "NeedsReview";
    riskScore: number;
    violations: Array<{
      clause: string;
      severity: string;
      reason: string;
    }>;
    suggestedRemediation: string;
    documentIntent: string;
    matchedPolicies: Array<{
      id: string;
      title: string;
      text: string;
      similarity: number;
    }>;
    completedAt: string;
  } | null;
  error?: string | null;
  createdAt: string;
  updatedAt: string;
};

export async function fetchAudits(take = 50) {
  return requestJson(apiUrl(`/api/audits?take=${take}`)) as Promise<BackendAuditRecord[]>;
}

export async function fetchAuditRecord(auditId: string) {
  return requestJson(apiUrl(`/api/audits/${auditId}`)) as Promise<BackendAuditRecord>;
}

export type AuditChatMessage = {
  role: "assistant" | "user";
  content: string;
};

export type AuditChatResponse = {
  message: string;
  answeredFromContext: boolean;
  citations: Array<{
    chunkIndex: number;
    text: string;
    similarity: number;
  }>;
  suggestions: string[];
};

export async function chatWithAudit(auditId: string, message: string, history: AuditChatMessage[] = []) {
  return requestJson(apiUrl(`/api/audits/${auditId}/chat`), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ message, history })
  }) as Promise<AuditChatResponse>;
}
