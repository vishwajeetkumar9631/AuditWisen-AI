"use client";

import { ChangeEvent, FormEvent, useMemo, useState } from "react";
import {
  AlertCircle,
  CheckCircle2,
  Database,
  FileText,
  FileUp,
  Gauge,
  Loader2,
  RefreshCw,
  Send,
  ShieldAlert,
  TextCursorInput
} from "lucide-react";
import { API_URL, auditFilePath, auditTextPath, fetchAuditStatus, probeBackend, submitAuditFile, submitAuditText } from "@/lib/api-client";
import { Button } from "./ui/button";

type SubmitState = "idle" | "submitting" | "polling" | "success" | "error";

type AcceptedAudit = {
  auditId: string;
  status: string;
  statusUrl: string;
  acceptedAt: string;
};

type AuditRecord = {
  id: string;
  status: "Queued" | "Processing" | "Completed" | "Failed";
  request?: {
    payload?: string;
    contentType?: string;
    sourceSystem?: string;
    correlationId?: string | null;
  };
  result?: {
    complianceStatus: string;
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

function isAcceptedAudit(value: unknown): value is AcceptedAudit {
  return Boolean(value && typeof value === "object" && typeof (value as AcceptedAudit).statusUrl === "string");
}

function isAuditRecord(value: unknown): value is AuditRecord {
  return Boolean(value && typeof value === "object" && typeof (value as AuditRecord).id === "string");
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function statusTone(status?: string) {
  if (status === "Completed") return "border-emerald-200 bg-emerald-50 text-ok";
  if (status === "Failed") return "border-red-200 bg-red-50 text-risk";
  if (status === "Processing") return "border-blue-200 bg-blue-50 text-azure";
  return "border-slate-200 bg-slate-50 text-graphite";
}

async function readPreview(file: File) {
  if (file.type.startsWith("text/") || /\.(txt|md|csv|json|xml|log)$/i.test(file.name)) {
    return (await file.text()).slice(0, 5000);
  }

  return "";
}

type AuditUploadFormProps = {
  onAuditReady?: (auditId: string | null) => void;
};

export function AuditUploadForm({ onAuditReady }: AuditUploadFormProps) {
  const [title, setTitle] = useState("");
  const [payload, setPayload] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [filePreview, setFilePreview] = useState("");
  const [state, setState] = useState<SubmitState>("idle");
  const [error, setError] = useState("");
  const [acceptedAudit, setAcceptedAudit] = useState<AcceptedAudit | null>(null);
  const [auditRecord, setAuditRecord] = useState<AuditRecord | null>(null);
  const [connectionMessage, setConnectionMessage] = useState("");

  const selectedSource = file ? "file" : payload.trim() ? "text" : "empty";
  const documentLabel = file?.name || title || "Untitled audit";
  const latestStatus = auditRecord?.status ?? acceptedAudit?.status;
  const result = auditRecord?.result;

  const payloadPreview = useMemo(() => {
    if (file) return filePreview || "Preview is available for text-based files. PDF text is extracted by the backend during analysis.";
    return payload.trim() || "Paste text or choose a document to preview it here.";
  }, [file, filePreview, payload]);

  async function onFileChange(event: ChangeEvent<HTMLInputElement>) {
    const nextFile = event.target.files?.[0] ?? null;
    setFile(nextFile);
    setFilePreview("");
    setError("");
    setAcceptedAudit(null);
    setAuditRecord(null);
    onAuditReady?.(null);

    if (nextFile) {
      setFilePreview(await readPreview(nextFile));
    }
  }

  async function pollStatus(statusUrl: string) {
    for (let attempt = 0; attempt < 30; attempt++) {
      const response = await fetchAuditStatus(statusUrl);
      if (isAuditRecord(response)) {
        setAuditRecord(response);
        onAuditReady?.(response.id);
        if (response.status === "Completed" || response.status === "Failed") {
          setState(response.status === "Completed" ? "success" : "error");
          if (response.error) setError(response.error);
          return;
        }
      }

      await new Promise((resolve) => setTimeout(resolve, 1000));
    }

    setState("polling");
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setConnectionMessage("");
    setAcceptedAudit(null);
    setAuditRecord(null);
    onAuditReady?.(null);

    if (!payload.trim() && !file) {
      setState("error");
      setError("Paste text or choose a file before starting an audit.");
      return;
    }

    try {
      setState("submitting");
      const response = file ? await submitAuditFile(file, title || file.name) : await submitAuditText(payload, title);

      if (!isAcceptedAudit(response)) {
        throw new Error("Backend response did not include an audit status URL.");
      }

      setAcceptedAudit(response);
      onAuditReady?.(response.auditId);
      setState("polling");
      await pollStatus(response.statusUrl);
    } catch (submitError) {
      setState("error");
      setError(submitError instanceof Error ? submitError.message : "Unable to submit audit.");
    }
  }

  async function onRefreshStatus() {
    if (!acceptedAudit?.statusUrl) return;

    try {
      setError("");
      const response = await fetchAuditStatus(acceptedAudit.statusUrl);
      if (isAuditRecord(response)) {
        setAuditRecord(response);
        onAuditReady?.(response.id);
        setState(response.status === "Completed" ? "success" : response.status === "Failed" ? "error" : "polling");
      }
    } catch (statusError) {
      setError(statusError instanceof Error ? statusError.message : "Unable to fetch audit status.");
    }
  }

  async function onTestConnection() {
    setError("");
    setConnectionMessage("");
    const health = await probeBackend();

    if (health.ok) {
      setConnectionMessage("Backend responded successfully.");
      return;
    }

    setState("error");
    setError(health.message ?? "Backend connection test failed.");
  }

  return (
    <div className="mt-6 space-y-6">
      <div className="grid gap-6 lg:grid-cols-[0.85fr_1.15fr]">
        <section className="glass-card rounded-md p-5">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-sm font-semibold uppercase tracking-wide text-azure">Document input</p>
              <h2 className="mt-1 text-xl font-semibold text-ink">Review source</h2>
            </div>
            <span className="rounded border border-blue-100 bg-blue-50 px-2.5 py-1 text-xs font-semibold text-azure">
              {selectedSource === "file" ? "File upload" : selectedSource === "text" ? "Text paste" : "Waiting"}
            </span>
          </div>

          <div className="mt-5 rounded-md border border-dashed border-azure/50 bg-white/44 p-5 text-center">
            <div className="mx-auto grid h-12 w-12 place-items-center rounded-md bg-azure/10 text-azure">
              <FileUp className="h-6 w-6" aria-hidden="true" />
            </div>
            <p className="mt-3 text-sm font-semibold text-ink">Upload PDF, TXT, JSON, XML, CSV, log, diff, patch, or image files.</p>
            <p className="mt-1 text-xs text-slate-600">Accepted documents are stored as audit records in the backend LiteDB database.</p>
            <label className="mt-4 inline-flex h-10 cursor-pointer items-center justify-center gap-2 rounded-md border border-line bg-white/70 px-4 text-sm font-semibold text-ink transition hover:bg-white">
              <FileText className="h-4 w-4" aria-hidden="true" />
              Browse File
              <input className="sr-only" type="file" onChange={onFileChange} />
            </label>
          </div>

          {file ? (
            <div className="mt-4 rounded-md border border-line bg-white/58 p-3 text-sm">
              <div className="flex items-center justify-between gap-3">
                <span className="truncate font-semibold text-ink">{file.name}</span>
                <span className="shrink-0 text-xs text-slate-600">{formatBytes(file.size)}</span>
              </div>
              <div className="mt-1 text-xs text-slate-600">{file.type || "Unknown content type"}</div>
            </div>
          ) : null}

          <label className="mt-5 block text-sm font-semibold text-ink" htmlFor="audit-text">
            Paste text instead
          </label>
          <div className="glass-control mt-2 flex min-h-44 rounded-md focus-within:border-azure focus-within:ring-2 focus-within:ring-azure/20">
            <div className="border-r border-white/60 bg-white/36 p-3 text-graphite">
              <TextCursorInput className="h-4 w-4" aria-hidden="true" />
            </div>
            <textarea
              id="audit-text"
              value={payload}
              onChange={(event) => {
                setPayload(event.target.value);
                setFile(null);
                setFilePreview("");
              }}
              className="min-h-44 flex-1 resize-y border-0 bg-transparent p-3 text-sm leading-6 outline-none"
              placeholder="Paste contract text, policy appendix, or customer terms..."
            />
          </div>

          <div className="mt-5">
            <div className="mb-2 flex items-center justify-between">
              <span className="text-sm font-semibold text-ink">Document preview</span>
              <span className="text-xs text-slate-600">{documentLabel}</span>
            </div>
            <pre className="max-h-64 overflow-auto whitespace-pre-wrap rounded-md border border-line bg-white/70 p-3 text-xs leading-5 text-graphite">
              {payloadPreview}
            </pre>
          </div>
        </section>

        <section className="glass-card rounded-md p-5">
          <div className="rounded-md border border-azure/20 bg-white/50 px-3 py-2 text-xs font-semibold text-graphite">
            Backend: {API_URL} - Text: {auditTextPath} - File: {auditFilePath}
          </div>

          <form onSubmit={onSubmit}>
            <label className="mt-5 block text-sm font-semibold text-ink" htmlFor="audit-title">
              Document title
            </label>
            <input
              id="audit-title"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              className="glass-control mt-2 h-11 w-full rounded-md px-3 text-sm outline-none focus:border-azure focus:ring-2 focus:ring-azure/20"
              placeholder="Cloud Services Master Agreement"
            />

            {error ? (
              <div className="mt-4 flex gap-2 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-risk">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                {error}
              </div>
            ) : null}

            {connectionMessage ? (
              <div className="mt-4 flex gap-2 rounded-md border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-ok">
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                {connectionMessage}
              </div>
            ) : null}

            <div className="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-3">
              <div className="rounded-md border border-line bg-white/58 p-3">
                <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-graphite">
                  <Database className="h-4 w-4" aria-hidden="true" />
                  Saved audit
                </div>
                <div className="mt-2 truncate text-sm font-semibold text-ink">{acceptedAudit?.auditId ?? "--"}</div>
              </div>
              <div className="rounded-md border border-line bg-white/58 p-3">
                <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-graphite">
                  <RefreshCw className="h-4 w-4" aria-hidden="true" />
                  Status
                </div>
                <div className={`mt-2 inline-flex rounded border px-2 py-1 text-xs font-semibold ${statusTone(latestStatus)}`}>
                  {latestStatus ?? "Not submitted"}
                </div>
              </div>
              <div className="rounded-md border border-line bg-white/58 p-3">
                <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-graphite">
                  <Gauge className="h-4 w-4" aria-hidden="true" />
                  Risk
                </div>
                <div className="mt-2 text-xl font-semibold text-ink">{result?.riskScore ?? "--"}</div>
              </div>
            </div>

            <div className="mt-5 flex flex-wrap justify-end gap-2">
              <Button type="button" variant="secondary" onClick={onTestConnection}>
                Test Backend
              </Button>
              {acceptedAudit?.statusUrl ? (
                <Button type="button" variant="secondary" onClick={onRefreshStatus}>
                  <RefreshCw className="h-4 w-4" />
                  Refresh
                </Button>
              ) : null}
              <Button type="submit" disabled={state === "submitting" || state === "polling"}>
                {state === "submitting" || state === "polling" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
                {state === "polling" ? "Evaluating" : "Analyze Document"}
              </Button>
            </div>
          </form>

          {acceptedAudit ? (
            <div className="mt-5 rounded-md border border-line bg-white/58 p-3 text-xs text-graphite">
              <div className="font-semibold text-ink">Backend storage</div>
              <div className="mt-1 break-all">Status URL: {acceptedAudit.statusUrl}</div>
              <div className="mt-1">Accepted at: {new Date(acceptedAudit.acceptedAt).toLocaleString()}</div>
            </div>
          ) : null}
        </section>
      </div>

      <section className="glass-card rounded-md p-5">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-sm font-semibold uppercase tracking-wide text-azure">Audit result</p>
            <h2 className="mt-1 text-xl font-semibold text-ink">Evaluation panel</h2>
          </div>
          <div className={`inline-flex w-fit rounded border px-2.5 py-1 text-xs font-semibold ${statusTone(latestStatus)}`}>
            {latestStatus ?? "No audit yet"}
          </div>
        </div>

        {result ? (
          <div className="mt-5 grid gap-5 lg:grid-cols-[0.85fr_1.15fr]">
            <div className="space-y-4">
              <div className="rounded-md border border-line bg-white/58 p-4">
                <div className="flex items-center justify-between">
                  <div>
                    <div className="text-xs font-semibold uppercase tracking-wide text-graphite">Compliance</div>
                    <div className="mt-1 text-2xl font-semibold text-ink">{result.complianceStatus}</div>
                  </div>
                  <ShieldAlert className="h-8 w-8 text-risk" aria-hidden="true" />
                </div>
                <div className="mt-4 h-2 rounded-full bg-blue-100">
                  <div
                    className={`h-2 rounded-full ${result.riskScore >= 70 ? "bg-risk" : result.riskScore >= 40 ? "bg-amber" : "bg-ok"}`}
                    style={{ width: `${Math.min(Math.max(result.riskScore, 0), 100)}%` }}
                  />
                </div>
                <div className="mt-2 text-sm text-graphite">Risk score {result.riskScore}</div>
              </div>

              <div className="rounded-md border border-line bg-white/58 p-4">
                <div className="text-xs font-semibold uppercase tracking-wide text-graphite">Document intent</div>
                <div className="mt-1 text-lg font-semibold text-ink">{result.documentIntent}</div>
                <div className="mt-3 text-sm leading-6 text-graphite">{result.suggestedRemediation}</div>
              </div>
            </div>

            <div className="space-y-4">
              <div>
                <h3 className="text-sm font-semibold uppercase tracking-wide text-graphite">Violations</h3>
                <div className="mt-2 space-y-2">
                  {result.violations.length > 0 ? (
                    result.violations.map((violation, index) => (
                      <div key={`${violation.clause}-${index}`} className="rounded-md border border-red-100 bg-red-50/70 p-3">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div className="font-semibold text-ink">{violation.clause}</div>
                          <span className="rounded border border-red-200 bg-white/70 px-2 py-0.5 text-xs font-semibold text-risk">
                            {violation.severity}
                          </span>
                        </div>
                        <p className="mt-2 text-sm leading-6 text-graphite">{violation.reason}</p>
                      </div>
                    ))
                  ) : (
                    <div className="rounded-md border border-emerald-100 bg-emerald-50 p-3 text-sm text-ok">No violations detected.</div>
                  )}
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold uppercase tracking-wide text-graphite">Matched policies</h3>
                <div className="mt-2 space-y-2">
                  {result.matchedPolicies.map((policy) => (
                    <div key={policy.id} className="rounded-md border border-line bg-white/58 p-3">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="font-semibold text-ink">{policy.title}</div>
                        <span className="rounded border border-blue-100 bg-blue-50 px-2 py-0.5 text-xs font-semibold text-azure">
                          {(policy.similarity * 100).toFixed(1)}%
                        </span>
                      </div>
                      <div className="mt-1 text-xs font-semibold text-graphite">{policy.id}</div>
                      <p className="mt-2 text-sm leading-6 text-graphite">{policy.text}</p>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </div>
        ) : (
          <div className="mt-5 rounded-md border border-line bg-white/58 p-6 text-center text-sm text-slate-600">
            Submit a document to see the saved audit record, risk score, violations, and matched RAG policies here.
          </div>
        )}
      </section>
    </div>
  );
}
