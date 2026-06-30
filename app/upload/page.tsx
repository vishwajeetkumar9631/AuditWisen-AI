"use client";

import { useState } from "react";
import { AuditUploadForm } from "@/components/audit-upload-form";
import { CollapsibleAuditWorkspace } from "@/components/collapsible-audit-workspace";

export default function UploadPage() {
  const [activeAuditId, setActiveAuditId] = useState<string | null>(null);

  return (
    <main className="mx-auto max-w-7xl space-y-6 px-4 py-6 sm:px-6 lg:px-8">
      <section className="glass-panel rounded-md p-5">
        <p className="text-sm font-semibold uppercase tracking-wide text-azure">Ingestion portal</p>
        <h1 className="mt-2 text-3xl font-semibold text-ink">Submit a Document Audit</h1>
        <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-600">
          Upload a contract or paste text to enqueue a background audit through the .NET API.
        </p>

        <AuditUploadForm onAuditReady={setActiveAuditId} />
      </section>
      <CollapsibleAuditWorkspace auditId={activeAuditId} />
    </main>
  );
}
