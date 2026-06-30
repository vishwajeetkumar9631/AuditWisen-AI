"use client";

import Link from "next/link";
import { ArrowUpDown, ExternalLink, Loader2, Search } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { AuditStream } from "@/components/audit-stream";
import { StatusBadge } from "@/components/status-badge";
import { fetchAudits } from "@/lib/api-client";
import { mapBackendAudit } from "@/lib/backend-audits";
import type { Audit } from "@/lib/audits";

export default function AuditsPage() {
  const [audits, setAudits] = useState<Audit[]>([]);
  const [query, setQuery] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let canceled = false;

    async function loadAudits() {
      try {
        const records = await fetchAudits(100);
        if (!canceled) {
          setAudits(records.map(mapBackendAudit));
          setError("");
        }
      } catch (loadError) {
        if (!canceled) {
          setError(loadError instanceof Error ? loadError.message : "Unable to load audit history.");
        }
      } finally {
        if (!canceled) {
          setIsLoading(false);
        }
      }
    }

    void loadAudits();
    const timer = window.setInterval(loadAudits, 5000);

    return () => {
      canceled = true;
      window.clearInterval(timer);
    };
  }, []);

  const filteredAudits = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    if (!normalized) return audits;

    return audits.filter((audit) =>
      [audit.title, audit.id, audit.owner, audit.status, audit.policy, audit.summary]
        .some((value) => value.toLowerCase().includes(normalized))
    );
  }, [audits, query]);

  return (
    <main className="mx-auto grid max-w-7xl gap-6 px-4 py-6 sm:px-6 lg:grid-cols-[1fr_360px] lg:px-8">
      <section className="glass-panel rounded-md">
        <div className="flex flex-col gap-4 border-b border-white/60 p-5 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <p className="text-sm font-semibold uppercase tracking-wide text-azure">Queue and history</p>
            <h1 className="mt-2 text-2xl font-semibold text-ink">Document Audits</h1>
          </div>
          <div className="glass-control flex h-10 min-w-0 items-center gap-2 rounded-md px-3">
            <Search className="h-4 w-4 text-graphite" aria-hidden="true" />
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              className="w-full min-w-0 border-0 bg-transparent text-sm outline-none"
              placeholder="Search audits"
            />
          </div>
        </div>
        {error ? <div className="border-b border-red-100 bg-red-50 px-5 py-3 text-sm text-risk">{error}</div> : null}
        <div className="overflow-x-auto">
          <table className="w-full min-w-[760px] border-collapse text-left">
            <thead>
              <tr className="border-b border-white/60 bg-white/42 text-xs uppercase tracking-wide text-graphite">
                <th className="px-5 py-3 font-semibold">Document</th>
                <th className="px-5 py-3 font-semibold">Status</th>
                <th className="px-5 py-3 font-semibold">
                  <span className="inline-flex items-center gap-1">
                    Risk <ArrowUpDown className="h-3 w-3" aria-hidden="true" />
                  </span>
                </th>
                <th className="px-5 py-3 font-semibold">Policy</th>
                <th className="px-5 py-3 font-semibold">Updated</th>
                <th className="px-5 py-3 font-semibold">Open</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-white/60 bg-white/38">
              {isLoading ? (
                <tr>
                  <td colSpan={6} className="px-5 py-8 text-center text-sm text-slate-600">
                    <Loader2 className="mx-auto mb-2 h-5 w-5 animate-spin text-azure" aria-hidden="true" />
                    Loading audit history
                  </td>
                </tr>
              ) : null}
              {!isLoading && filteredAudits.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-5 py-8 text-center text-sm text-slate-600">
                    No saved audits found.
                  </td>
                </tr>
              ) : null}
              {filteredAudits.map((audit) => (
                <tr key={audit.id} className="transition hover:bg-white/62">
                  <td className="px-5 py-4">
                    <div className="font-semibold text-ink">{audit.title}</div>
                    <div className="mt-1 text-xs text-slate-600">
                      {audit.id} - {audit.owner}
                    </div>
                  </td>
                  <td className="px-5 py-4">
                    <StatusBadge status={audit.status} compact />
                  </td>
                  <td className="px-5 py-4">
                    <span className="font-semibold text-ink">{audit.riskScore || "--"}</span>
                  </td>
                  <td className="max-w-56 px-5 py-4 text-sm text-graphite">{audit.policy}</td>
                  <td className="px-5 py-4 text-sm text-graphite">{audit.updatedAt}</td>
                  <td className="px-5 py-4">
                    <Link
                      href={`/audits/${audit.id}`}
                      className="glass-control inline-flex h-9 w-9 items-center justify-center rounded-md text-graphite transition hover:bg-white/90 hover:text-azure"
                      aria-label={`Open ${audit.title}`}
                    >
                      <ExternalLink className="h-4 w-4" aria-hidden="true" />
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
      <AuditStream className="h-max" />
    </main>
  );
}
