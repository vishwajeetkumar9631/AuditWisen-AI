"use client";

import { Check, Copy, Wand2 } from "lucide-react";
import { useState } from "react";
import { violations } from "@/lib/audits";
import { Button } from "./ui/button";

const documentText = [
  "This Cloud Services Master Agreement governs access to the platform and related support services.",
  "The provider may appoint subprocessors at its discretion and will publish material changes after they become effective.",
  "Customer data may be transferred to jurisdictions selected by the provider to support normal service operations.",
  "Each party will maintain commercially reasonable safeguards designed to protect confidential information."
];

export function DiffViewer() {
  const [applied, setApplied] = useState(false);
  const primary = violations[0];

  return (
    <div className="glass-panel grid min-h-[620px] grid-cols-1 overflow-hidden rounded-md lg:grid-cols-[1.15fr_0.85fr]">
      <section className="border-b border-white/60 lg:border-b-0 lg:border-r">
        <div className="border-b border-white/60 px-5 py-4">
          <h2 className="text-base font-semibold text-ink">Original Document</h2>
          <p className="mt-1 text-sm text-slate-600">Non-compliant language is highlighted inline.</p>
        </div>
        <article className="space-y-5 p-5 text-[15px] leading-7 text-graphite">
          {documentText.map((line) => {
            const isPrimary = line === primary.excerpt;
            const isSecondary = line === violations[1].excerpt;
            return (
              <p
                key={line}
                className={
                  isPrimary
                    ? "rounded-md border border-red-200 bg-red-50 px-3 py-2 text-ink"
                    : isSecondary
                      ? "rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-ink"
                      : undefined
                }
              >
                {line}
              </p>
            );
          })}
        </article>
      </section>
      <aside className="bg-blue-50/38">
        <div className="border-b border-white/60 bg-white/42 px-5 py-4">
          <h2 className="text-base font-semibold text-ink">AI Remediation</h2>
          <p className="mt-1 text-sm text-slate-600">Policy evidence, risk score, and proposed contract patch.</p>
        </div>
        <div className="space-y-4 p-5">
          {violations.map((violation) => (
            <div key={violation.id} className="glass-card rounded-md p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-graphite">{violation.clause}</p>
                  <h3 className="mt-1 text-sm font-semibold text-ink">{violation.policy}</h3>
                </div>
                <span className="rounded border border-line px-2 py-1 text-xs font-semibold text-risk">
                  {violation.riskScore}
                </span>
              </div>
              <p className="mt-3 text-sm leading-6 text-graphite">{violation.suggestion}</p>
            </div>
          ))}
          <div className="glass-card rounded-md border-azure/30 p-4">
            <div className="flex items-center gap-2 text-sm font-semibold text-ink">
              <Wand2 className="h-4 w-4 text-azure" aria-hidden="true" />
              Suggested Patch
            </div>
            <p className="mt-3 text-sm leading-6 text-graphite">{primary.suggestion}</p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button onClick={() => setApplied(true)}>
                {applied ? <Check className="h-4 w-4" /> : <Wand2 className="h-4 w-4" />}
                {applied ? "Patch Applied" : "Apply Patch"}
              </Button>
              <Button variant="secondary">
                <Copy className="h-4 w-4" />
                Copy Text
              </Button>
            </div>
          </div>
        </div>
      </aside>
    </div>
  );
}
