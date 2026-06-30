import { audits, statusMeta, type AuditStatus } from "@/lib/audits";
import { cn } from "@/lib/utils";

type StatusBadgeProps = {
  status: AuditStatus;
  compact?: boolean;
};

export function StatusBadge({ status, compact = false }: StatusBadgeProps) {
  const meta = statusMeta[status];
  const Icon = meta.icon;

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded border px-2.5 py-1 text-xs font-semibold",
        meta.className,
        compact && "px-2"
      )}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      {compact ? status.replace("Analyzing Policies", "Analyzing") : status}
    </span>
  );
}

export function StatusSummary() {
  const counts = audits.reduce(
    (acc, audit) => {
      acc[audit.status] += 1;
      return acc;
    },
    { Processing: 0, "Analyzing Policies": 0, Flagged: 0, Passed: 0 } as Record<AuditStatus, number>
  );

  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      {Object.entries(counts).map(([status, count]) => (
        <div key={status} className="glass-card rounded-md p-4">
          <StatusBadge status={status as AuditStatus} compact />
          <div className="mt-3 text-3xl font-semibold text-ink">{count}</div>
        </div>
      ))}
    </div>
  );
}
