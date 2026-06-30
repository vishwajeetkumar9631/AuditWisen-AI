"use client";

import Link from "next/link";
import { Activity, Radio } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { fetchAudits } from "@/lib/api-client";
import { mapBackendAudit, mergeAuditList } from "@/lib/backend-audits";
import { type Audit } from "@/lib/audits";
import { cn } from "@/lib/utils";
import { useSignalR } from "@/hooks/useSignalR";
import { StatusBadge } from "./status-badge";

const API_URL = process.env.NEXT_PUBLIC_API_URL;
const SIGNALR_HUB_PATH = process.env.NEXT_PUBLIC_SIGNALR_HUB_PATH;

export function AuditStream({ className }: { className?: string }) {
  const hubUrl = API_URL && SIGNALR_HUB_PATH ? `${API_URL}${SIGNALR_HUB_PATH}` : "";
  const { liveUpdate, connectionState } = useSignalR(hubUrl);
  const [items, setItems] = useState<Audit[]>([]);
  const [loadError, setLoadError] = useState("");

  useEffect(() => {
    let canceled = false;

    async function loadAudits() {
      try {
        const records = await fetchAudits(20);
        if (!canceled) {
          setItems(records.map(mapBackendAudit));
          setLoadError("");
        }
      } catch (error) {
        if (!canceled) {
          setLoadError(error instanceof Error ? error.message : "Unable to load audit history.");
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

  useEffect(() => {
    if (!liveUpdate) return;
    setItems((current) =>
      mergeAuditList(current, {
        id: liveUpdate.id,
        title: liveUpdate.title ?? `Audit ${liveUpdate.id.slice(0, 8)}`,
        owner: liveUpdate.owner ?? "Backend",
        status: liveUpdate.status ?? "Processing",
        riskScore: liveUpdate.riskScore ?? 0,
        policy: liveUpdate.policy ?? "Policy retrieval",
        category: liveUpdate.category ?? "Compliance",
        submittedAt: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
        updatedAt: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
        summary: liveUpdate.summary ?? liveUpdate.message ?? "Audit update received."
      })
    );
  }, [liveUpdate]);

  const connectionLabel = useMemo(() => {
    if (connectionState === "Connected") return "SignalR live";
    if (connectionState === "Reconnecting") return "Reconnecting";
    return items.length > 0 ? "Polling DB" : "Waiting";
  }, [connectionState, items.length]);

  return (
    <section className={cn("glass-panel rounded-md", className)}>
      <div className="flex items-center justify-between border-b border-white/60 px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-graphite">Live Audit Stream</h2>
          <p className="mt-1 text-sm text-slate-600">Queued and completed audits from the backend database.</p>
        </div>
        <span className="glass-control inline-flex items-center gap-2 rounded px-2.5 py-1 text-xs font-semibold text-graphite">
          <Radio className="h-3.5 w-3.5 text-azure" aria-hidden="true" />
          {connectionLabel}
        </span>
      </div>
      <div className="divide-y divide-white/60">
        {loadError ? <div className="px-4 py-3 text-sm text-risk">{loadError}</div> : null}
        {items.length === 0 && !loadError ? <div className="px-4 py-6 text-sm text-slate-600">No saved audits yet.</div> : null}
        {items.map((audit) => (
          <Link key={audit.id} href={`/audits/${audit.id}`} className="block px-4 py-4 transition hover:bg-white/42">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <Activity className="h-4 w-4 text-azure" aria-hidden="true" />
                  <p className="truncate text-sm font-semibold text-ink">{audit.title}</p>
                </div>
                <p className="mt-1 text-xs text-slate-600">
                  {audit.id} - {audit.owner} - updated {audit.updatedAt}
                </p>
              </div>
              <StatusBadge status={audit.status} compact />
            </div>
            <div className="mt-3 flex items-center gap-3">
              <div className="h-2 flex-1 rounded-full bg-blue-100/80">
                <div
                  className={cn("h-2 rounded-full", audit.riskScore > 70 ? "bg-risk" : audit.riskScore > 40 ? "bg-amber" : "bg-ok")}
                  style={{ width: `${Math.max(audit.riskScore, audit.status === "Processing" ? 18 : 6)}%` }}
                />
              </div>
              <span className="w-12 text-right text-xs font-semibold text-graphite">{audit.riskScore || "--"}</span>
            </div>
          </Link>
        ))}
      </div>
    </section>
  );
}
