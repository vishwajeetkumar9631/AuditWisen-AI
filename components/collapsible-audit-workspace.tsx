"use client";

import { FormEvent, useMemo, useRef, useState } from "react";
import {
  Bot,
  ChevronRight,
  FileText,
  Loader2,
  MessageSquareText,
  PanelRightOpen,
  Send,
  Sparkles,
  Wand2
} from "lucide-react";
import { chatWithAudit, type AuditChatMessage } from "@/lib/api-client";
import { violations } from "@/lib/audits";
import { cn } from "@/lib/utils";
import { Button } from "./ui/button";

type DocumentParagraph = {
  id: string;
  label: string;
  text: string;
  tone?: "risk" | "warning";
};

type ChatMessage = {
  id: string;
  role: "assistant" | "user";
  text: string;
  referenceId?: string;
  referenceLabel?: string;
};

type Suggestion = {
  id: string;
  label: string;
  answer: string;
  referenceId: string;
  referenceLabel: string;
};

const documentParagraphs: DocumentParagraph[] = [
  {
    id: "intro",
    label: "1. Services",
    text: "This Cloud Services Master Agreement governs access to the platform and related support services."
  },
  {
    id: "subprocessors",
    label: "6.3 Subprocessor Notifications",
    text: violations[0].excerpt,
    tone: "risk"
  },
  {
    id: "transfer",
    label: "7.1 Data Transfers",
    text: violations[1].excerpt,
    tone: "warning"
  },
  {
    id: "isolation",
    label: "8.2 Data Isolation",
    text:
      "Customer environments must remain logically isolated, and access to tenant data must be restricted to approved support workflows with auditable authorization."
  },
  {
    id: "indemnity",
    label: "12.4 Indemnification",
    text:
      "Each party will indemnify the other for third-party claims arising from gross negligence, willful misconduct, or unauthorized use of confidential information."
  },
  {
    id: "safeguards",
    label: "14.1 Safeguards",
    text: "Each party will maintain commercially reasonable safeguards designed to protect confidential information."
  }
];

const openingMessage: ChatMessage = {
  id: "assistant-opening",
  role: "assistant",
  text:
    "I found two policy conflicts in the current audit. Select a suggested prompt below, or click a referenced clause in my replies to jump the document viewer to the exact paragraph."
};

function buildSuggestions(): Suggestion[] {
  const violationPrompts = violations.map((violation, index) => ({
    id: `violation-${violation.id}`,
    label: `Explain ${violation.clause.split(",")[0]}`,
    answer: `${violation.policy} flags this language because ${violation.suggestion}`,
    referenceId: index === 0 ? "subprocessors" : "transfer",
    referenceLabel: violation.clause
  }));

  return [
    {
      id: "data-isolation",
      label: "Summarize all data isolation guidelines in this file.",
      answer:
        "The data isolation language requires tenant separation, approved support workflows, and auditable authorization before anyone can access customer data.",
      referenceId: "isolation",
      referenceLabel: "8.2 Data Isolation"
    },
    {
      id: "indemnity-draft",
      label: "Draft an alternative version of the indemnification clause.",
      answer:
        "Alternative: Each party will defend and indemnify the other against third-party claims caused by its breach of confidentiality, data protection duties, gross negligence, or willful misconduct.",
      referenceId: "indemnity",
      referenceLabel: "12.4 Indemnification"
    },
    ...violationPrompts
  ];
}

type CollapsibleAuditWorkspaceProps = {
  auditId?: string | null;
};

export function CollapsibleAuditWorkspace({ auditId }: CollapsibleAuditWorkspaceProps) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([openingMessage]);
  const [activeReference, setActiveReference] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [chatError, setChatError] = useState("");
  const [backendSuggestions, setBackendSuggestions] = useState<string[]>([]);
  const paragraphRefs = useRef<Record<string, HTMLParagraphElement | null>>({});
  const suggestions = useMemo(buildSuggestions, []);
  const suggestionLabels = backendSuggestions.length > 0 ? backendSuggestions : suggestions.map((suggestion) => suggestion.label);

  function focusParagraph(id: string) {
    setActiveReference(id);
    paragraphRefs.current[id]?.scrollIntoView({ behavior: "smooth", block: "center" });
    window.setTimeout(() => setActiveReference((current) => (current === id ? null : current)), 1600);
  }

  async function submitChat(messageText: string) {
    const question = messageText.trim();
    if (!question || isSending) {
      return;
    }

    setDrawerOpen(true);
    setDraft("");
    setChatError("");

    const history: AuditChatMessage[] = messages
      .filter((message) => message.id !== openingMessage.id)
      .slice(-8)
      .map((message) => ({
        role: message.role,
        content: message.text
      }));

    const userMessage: ChatMessage = {
      id: `user-${Date.now()}`,
      role: "user",
      text: question
    };

    setMessages((current) => [...current, userMessage]);

    if (!auditId) {
      setMessages((current) => [
        ...current,
        {
          id: `assistant-missing-audit-${Date.now()}`,
          role: "assistant",
          text: "Submit a document first. Once the backend creates an audit record, I can answer questions about that document."
        }
      ]);
      return;
    }

    try {
      setIsSending(true);
      const response = await chatWithAudit(auditId, question, history);
      setBackendSuggestions(response.suggestions ?? []);
      setMessages((current) => [
        ...current,
        {
          id: `assistant-${Date.now()}`,
          role: "assistant",
          text: response.message
        }
      ]);
    } catch (error) {
      const message = error instanceof Error ? error.message : "Unable to reach the audit assistant.";
      setChatError(message);
      setMessages((current) => [
        ...current,
        {
          id: `assistant-error-${Date.now()}`,
          role: "assistant",
          text: message
        }
      ]);
    } finally {
      setIsSending(false);
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void submitChat(draft);
  }

  return (
    <section className="relative overflow-hidden rounded-md border border-line bg-white/45 shadow-glass">
      <div className="flex flex-col gap-3 border-b border-white/70 bg-white/54 px-5 py-4 lg:flex-row lg:items-center lg:justify-between">
        <div>
          <p className="text-sm font-semibold uppercase tracking-wide text-azure">Review workspace</p>
          <h2 className="mt-1 text-2xl font-semibold text-ink">Document review with collapsible AI chat</h2>
        </div>
        <Button type="button" onClick={() => setDrawerOpen(true)} className="w-full sm:w-fit">
          <PanelRightOpen className="h-4 w-4" aria-hidden="true" />
          Open Assistant
        </Button>
      </div>

      <div className="grid min-h-[680px] grid-cols-1 lg:grid-cols-[minmax(0,1.3fr)_minmax(320px,0.7fr)]">
        <section className="min-w-0 border-b border-white/70 lg:border-b-0 lg:border-r">
          <div className="flex items-center justify-between gap-3 border-b border-white/70 px-5 py-3">
            <div className="flex items-center gap-2 text-sm font-semibold text-ink">
              <FileText className="h-4 w-4 text-azure" aria-hidden="true" />
              Original document
            </div>
            <span className="rounded border border-red-100 bg-red-50 px-2 py-1 text-xs font-semibold text-risk">
              {violations.length} open flags
            </span>
          </div>

          <article className="max-h-[620px] space-y-4 overflow-auto px-5 py-5 text-[15px] leading-7 text-graphite">
            {documentParagraphs.map((paragraph) => (
              <p
                key={paragraph.id}
                ref={(node) => {
                  paragraphRefs.current[paragraph.id] = node;
                }}
                className={cn(
                  "rounded-md border px-4 py-3 transition",
                  paragraph.tone === "risk" && "border-red-200 bg-red-50/80 text-ink",
                  paragraph.tone === "warning" && "border-amber-200 bg-amber-50/80 text-ink",
                  !paragraph.tone && "border-line bg-white/66",
                  activeReference === paragraph.id && "audit-reference-flash"
                )}
              >
                <span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-graphite">{paragraph.label}</span>
                {paragraph.text}
              </p>
            ))}
          </article>
        </section>

        <aside className="bg-skyglass/60">
          <div className="border-b border-white/70 px-5 py-3">
            <div className="flex items-center gap-2 text-sm font-semibold text-ink">
              <Sparkles className="h-4 w-4 text-azure" aria-hidden="true" />
              Audit findings
            </div>
          </div>
          <div className="space-y-3 p-5">
            {violations.map((violation, index) => {
              const referenceId = index === 0 ? "subprocessors" : "transfer";
              return (
                <button
                  type="button"
                  key={violation.id}
                  onClick={() => focusParagraph(referenceId)}
                  className="w-full rounded-md border border-line bg-white/70 p-4 text-left transition hover:border-azure/40 hover:bg-white"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-wide text-graphite">{violation.policy}</p>
                      <h3 className="mt-1 text-sm font-semibold text-ink">{violation.clause}</h3>
                    </div>
                    <span className="rounded border border-red-200 bg-red-50 px-2 py-0.5 text-xs font-semibold text-risk">
                      {violation.severity}
                    </span>
                  </div>
                  <p className="mt-3 text-sm leading-6 text-graphite">{violation.suggestion}</p>
                </button>
              );
            })}

            <div className="rounded-md border border-azure/20 bg-blue-50/60 p-4">
              <div className="flex items-center gap-2 text-sm font-semibold text-ink">
                <Wand2 className="h-4 w-4 text-azure" aria-hidden="true" />
                Suggested remediation
              </div>
              <p className="mt-2 text-sm leading-6 text-graphite">{violations[0].suggestion}</p>
            </div>
          </div>
        </aside>
      </div>

      <div
        className={cn(
          "fixed inset-y-0 right-0 z-50 flex w-full max-w-[440px] transform flex-col border-l border-line bg-white shadow-2xl transition-transform duration-300",
          drawerOpen ? "translate-x-0" : "translate-x-full"
        )}
        aria-hidden={!drawerOpen}
      >
        <div className="flex items-center justify-between border-b border-line px-4 py-3">
          <div className="flex items-center gap-2">
            <span className="grid h-9 w-9 place-items-center rounded-md bg-azure text-white">
              <Bot className="h-5 w-5" aria-hidden="true" />
            </span>
            <div>
              <h3 className="text-sm font-semibold text-ink">Audit assistant</h3>
              <p className="text-xs text-graphite">Reference-aware chat</p>
            </div>
          </div>
          <button
            type="button"
            onClick={() => setDrawerOpen(false)}
            className="grid h-9 w-9 place-items-center rounded-md border border-line text-graphite transition hover:bg-slate-50"
            aria-label="Close assistant"
          >
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>

        <div className="flex-1 space-y-3 overflow-auto bg-slate-50/70 p-4">
          {messages.map((message) => (
            <div key={message.id} className={cn("flex", message.role === "user" ? "justify-end" : "justify-start")}>
              <div
                className={cn(
                  "max-w-[86%] rounded-md border px-3 py-2 text-sm leading-6",
                  message.role === "user" ? "border-azure bg-azure text-white" : "border-line bg-white text-graphite"
                )}
              >
                <p>{message.text}</p>
                {message.referenceId ? (
                  <button
                    type="button"
                    onClick={() => focusParagraph(message.referenceId!)}
                    className="mt-2 inline-flex items-center gap-1 rounded border border-azure/20 bg-blue-50 px-2 py-1 text-xs font-semibold text-azure transition hover:bg-blue-100"
                  >
                    <MessageSquareText className="h-3.5 w-3.5" aria-hidden="true" />
                    {message.referenceLabel}
                  </button>
                ) : null}
              </div>
            </div>
          ))}
        </div>

        <div className="border-t border-line bg-white p-4">
          <div className="mb-3 flex flex-wrap gap-2">
            {suggestionLabels.map((suggestion) => (
              <button
                type="button"
                key={suggestion}
                onClick={() => void submitChat(suggestion)}
                disabled={isSending}
                className="rounded-full border border-line bg-white px-3 py-1.5 text-left text-xs font-semibold text-graphite transition hover:border-azure/40 hover:bg-blue-50 hover:text-azure"
              >
                {suggestion}
              </button>
            ))}
          </div>
          {chatError ? <div className="mb-2 text-xs font-semibold text-risk">{chatError}</div> : null}
          <form onSubmit={handleSubmit} className="flex items-center gap-2 rounded-md border border-line bg-slate-50 px-3 py-2 text-sm text-slate-700 focus-within:border-azure focus-within:ring-2 focus-within:ring-azure/20">
            <input
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              className="min-w-0 flex-1 border-0 bg-transparent outline-none"
              placeholder="Ask about a clause or choose a suggestion"
              disabled={isSending}
            />
            <button
              type="submit"
              disabled={!draft.trim() || isSending}
              className="grid h-8 w-8 place-items-center rounded-md text-azure transition hover:bg-blue-50 disabled:cursor-not-allowed disabled:text-slate-400"
              aria-label="Send message"
            >
              {isSending ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Send className="h-4 w-4" aria-hidden="true" />}
            </button>
          </form>
        </div>
      </div>

      {drawerOpen ? (
        <button
          type="button"
          className="fixed inset-0 z-40 cursor-default bg-ink/18 backdrop-blur-[1px] lg:hidden"
          aria-label="Close assistant overlay"
          onClick={() => setDrawerOpen(false)}
        />
      ) : null}
    </section>
  );
}
