import type { BackendAuditRecord } from "./api-client";
import type { Audit, AuditStatus } from "./audits";

export function mapBackendAudit(record: BackendAuditRecord): Audit {
  const result = record.result;
  const title = record.request.correlationId || inferTitle(record);

  return {
    id: record.id,
    title,
    owner: record.request.sourceSystem || "Backend",
    status: mapStatus(record),
    riskScore: result?.riskScore ?? 0,
    policy: result?.matchedPolicies[0]?.title ?? "Policy retrieval",
    category: result?.documentIntent ?? "Compliance",
    submittedAt: formatTime(record.createdAt),
    updatedAt: formatTime(record.updatedAt),
    summary: record.error || result?.suggestedRemediation || "Audit is queued or processing."
  };
}

export function mergeAuditList(current: Audit[], next: Audit) {
  const exists = current.some((audit) => audit.id === next.id);
  const merged = exists ? current.map((audit) => (audit.id === next.id ? { ...audit, ...next } : audit)) : [next, ...current];
  return merged.slice(0, 50);
}

function mapStatus(record: BackendAuditRecord): AuditStatus {
  if (record.status === "Queued") return "Processing";
  if (record.status === "Processing") return "Analyzing Policies";
  if (record.status === "Failed") return "Flagged";
  return record.result?.complianceStatus === "Passed" ? "Passed" : "Flagged";
}

function inferTitle(record: BackendAuditRecord) {
  if (record.request.contentType?.includes("pdf")) {
    return "Uploaded PDF audit";
  }

  const firstLine = record.request.payload
    ?.split(/\r?\n/)
    .map((line) => line.trim())
    .find(Boolean);

  if (!firstLine) {
    return `Audit ${record.id.slice(0, 8)}`;
  }

  return firstLine.length > 72 ? `${firstLine.slice(0, 69)}...` : firstLine;
}

function formatTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "--";
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}
