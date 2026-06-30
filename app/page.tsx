import { Activity, Clock, FileWarning, Gauge } from "lucide-react";
import { AuditStream } from "@/components/audit-stream";
import { BreachChart, HoursSavedChart, RiskHeatmap, TrendChart } from "@/components/analytics-charts";
import { StatusSummary } from "@/components/status-badge";

const metrics = [
  { label: "Active audits", value: "42", icon: Activity },
  { label: "Avg risk score", value: "51", icon: Gauge },
  { label: "Open flags", value: "17", icon: FileWarning },
  { label: "Hours saved", value: "361", icon: Clock }
];

export default function DashboardPage() {
  return (
    <main className="mx-auto grid max-w-7xl gap-6 px-4 py-6 sm:px-6 lg:grid-cols-[1fr_390px] lg:px-8">
      <section className="space-y-6">
        <div className="glass-panel flex flex-col justify-between gap-4 rounded-md p-5 sm:flex-row sm:items-end">
          <div>
            <p className="text-sm font-semibold uppercase tracking-wide text-azure">Compliance analytics</p>
            <h1 className="mt-2 text-3xl font-semibold text-ink">AuditWise Control Room</h1>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-600">
              Track asynchronous document reviews, policy breaches, and remediation velocity from one live workspace.
            </p>
          </div>
          <div className="glass-control rounded-md px-3 py-2 text-sm text-graphite">
            API: {process.env.NEXT_PUBLIC_API_URL ?? "Demo mode"}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          {metrics.map((metric) => (
            <div key={metric.label} className="glass-card rounded-md p-4">
              <metric.icon className="h-5 w-5 text-azure" aria-hidden="true" />
              <div className="mt-4 text-3xl font-semibold text-ink">{metric.value}</div>
              <div className="mt-1 text-sm text-slate-600">{metric.label}</div>
            </div>
          ))}
        </div>

        <StatusSummary />
        <RiskHeatmap />
        <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
          <TrendChart />
          <HoursSavedChart />
        </div>
        <BreachChart />
      </section>
      <AuditStream className="h-max lg:sticky lg:top-24" />
    </main>
  );
}
