import Link from "next/link";
import { ArrowLeft, Database, FileText, ShieldAlert } from "lucide-react";
import { notFound } from "next/navigation";
import { CollapsibleAuditWorkspace } from "@/components/collapsible-audit-workspace";
import { StatusBadge } from "@/components/status-badge";
import { fetchAuditRecord } from "@/lib/api-client";
import { mapBackendAudit } from "@/lib/backend-audits";

type AuditDetailPageProps = {
  params: Promise<{ id: string }>;
};

export default async function AuditDetailPage({ params }: AuditDetailPageProps) {
  const { id } = await params;
  const record = await fetchAuditRecord(id).catch(() => null);

  if (!record) notFound();

  const audit = mapBackendAudit(record);
  const result = record.result;

  return (
    <main className="mx-auto max-w-7xl space-y-6 px-4 py-6 sm:px-6 lg:px-8">
      <div className="glass-panel flex flex-col gap-4 rounded-md p-5 lg:flex-row lg:items-center lg:justify-between">
        <div>
          <Link href="/audits" className="inline-flex items-center gap-2 text-sm font-semibold text-graphite hover:text-azure">
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            Audits
          </Link>
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <h1 className="text-2xl font-semibold text-ink">{audit.title}</h1>
            <StatusBadge status={audit.status} />
          </div>
          <p className="mt-2 text-sm leading-6 text-slate-600">{audit.summary}</p>
        </div>
        <div className="rounded-md border border-line bg-white/58 p-3 text-xs text-graphite">
          <div className="font-semibold text-ink">DB audit id</div>
          <div className="mt-1 break-all">{record.id}</div>
        </div>
      </div>

      <section className="grid gap-5 lg:grid-cols-[0.85fr_1.15fr]">
        <div className="glass-card rounded-md p-5">
          <div className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-graphite">
            <FileText className="h-4 w-4 text-azure" aria-hidden="true" />
            Document updated from DB
          </div>
          <pre className="mt-4 max-h-[520px] overflow-auto whitespace-pre-wrap rounded-md border border-line bg-white/70 p-4 text-sm leading-6 text-graphite">
            {record.request.payload || "No document text was stored for this audit."}
          </pre>
        </div>

        <div className="glass-card rounded-md p-5">
          <div className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-graphite">
            <Database className="h-4 w-4 text-azure" aria-hidden="true" />
            Evaluation from DB
          </div>
          {result ? (
            <div className="mt-4 space-y-4">
              <div className="rounded-md border border-line bg-white/58 p-4">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <div className="text-xs font-semibold uppercase tracking-wide text-graphite">Compliance</div>
                    <div className="mt-1 text-2xl font-semibold text-ink">{result.complianceStatus}</div>
                  </div>
                  <div className="text-right">
                    <div className="text-xs font-semibold uppercase tracking-wide text-graphite">Risk</div>
                    <div className="mt-1 text-2xl font-semibold text-ink">{result.riskScore}</div>
                  </div>
                </div>
                <p className="mt-3 text-sm leading-6 text-graphite">{result.suggestedRemediation}</p>
              </div>

              <div>
                <h2 className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-graphite">
                  <ShieldAlert className="h-4 w-4 text-risk" aria-hidden="true" />
                  Findings
                </h2>
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
                <h2 className="text-sm font-semibold uppercase tracking-wide text-graphite">Matched policies</h2>
                <div className="mt-2 space-y-2">
                  {result.matchedPolicies.map((policy) => (
                    <div key={policy.id} className="rounded-md border border-line bg-white/58 p-3">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="font-semibold text-ink">{policy.title}</div>
                        <span className="rounded border border-blue-100 bg-blue-50 px-2 py-0.5 text-xs font-semibold text-azure">
                          {(policy.similarity * 100).toFixed(1)}%
                        </span>
                      </div>
                      <p className="mt-2 text-sm leading-6 text-graphite">{policy.text}</p>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          ) : (
            <div className="mt-4 rounded-md border border-line bg-white/58 p-6 text-center text-sm text-slate-600">
              This audit is still queued or processing. The page will show the saved result once the worker completes it.
            </div>
          )}
        </div>
      </section>

      <CollapsibleAuditWorkspace auditId={record.id} />
    </main>
  );
}
