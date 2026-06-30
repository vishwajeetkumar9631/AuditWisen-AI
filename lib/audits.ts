import { AlertTriangle, CheckCircle2, FileSearch, Gavel, ShieldAlert } from "lucide-react";

export type AuditStatus = "Processing" | "Analyzing Policies" | "Flagged" | "Passed";

export type Audit = {
  id: string;
  title: string;
  owner: string;
  status: AuditStatus;
  riskScore: number;
  policy: string;
  category: string;
  submittedAt: string;
  updatedAt: string;
  summary: string;
};

export type Violation = {
  id: string;
  severity: "High" | "Medium" | "Low";
  policy: string;
  clause: string;
  excerpt: string;
  suggestion: string;
  riskScore: number;
};

export const audits: Audit[] = [
  {
    id: "AUD-1048",
    title: "Cloud Services Master Agreement",
    owner: "Legal Ops",
    status: "Flagged",
    riskScore: 88,
    policy: "Data Processing Addendum v4.2",
    category: "Privacy",
    submittedAt: "09:14",
    updatedAt: "09:18",
    summary: "Missing subprocessor notification window and broad data transfer language."
  },
  {
    id: "AUD-1047",
    title: "Vendor Security Questionnaire",
    owner: "Procurement",
    status: "Analyzing Policies",
    riskScore: 43,
    policy: "Security Control Baseline",
    category: "Security",
    submittedAt: "09:10",
    updatedAt: "09:17",
    summary: "Cross-checking encryption, SSO, and audit log commitments."
  },
  {
    id: "AUD-1046",
    title: "Enterprise Order Form",
    owner: "Sales",
    status: "Passed",
    riskScore: 12,
    policy: "Revenue Recognition Guardrails",
    category: "Finance",
    submittedAt: "08:56",
    updatedAt: "09:02",
    summary: "Payment terms, renewal wording, and acceptance language are compliant."
  },
  {
    id: "AUD-1045",
    title: "AI Feature Addendum",
    owner: "Product Counsel",
    status: "Processing",
    riskScore: 0,
    policy: "Responsible AI Review",
    category: "AI Governance",
    submittedAt: "08:48",
    updatedAt: "09:16",
    summary: "Queued behind long-running model output retention checks."
  },
  {
    id: "AUD-1044",
    title: "Partner Reseller Agreement",
    owner: "Channels",
    status: "Flagged",
    riskScore: 72,
    policy: "Partner Compliance Rules",
    category: "Commercial",
    submittedAt: "08:32",
    updatedAt: "08:45",
    summary: "Discount approval threshold exceeded without finance sign-off."
  }
];

export const violations: Violation[] = [
  {
    id: "V-901",
    severity: "High",
    policy: "Data Processing Addendum v4.2",
    clause: "Section 6.3, Subprocessor Notifications",
    excerpt:
      "The provider may appoint subprocessors at its discretion and will publish material changes after they become effective.",
    suggestion:
      "The provider must provide at least 30 days' advance notice before appointing a new subprocessor and maintain an objection workflow for documented privacy concerns.",
    riskScore: 88
  },
  {
    id: "V-902",
    severity: "Medium",
    policy: "Cross-Border Transfer Standard",
    clause: "Section 3.1, Transfer Mechanism",
    excerpt:
      "Customer data may be transferred to jurisdictions selected by the provider to support normal service operations.",
    suggestion:
      "Customer data may be transferred only under approved transfer mechanisms, including SCCs or an equivalent lawful framework documented in the data transfer register.",
    riskScore: 64
  }
];

export const riskCategories = [
  { name: "Privacy", risk: 88, audits: 17 },
  { name: "Security", risk: 66, audits: 24 },
  { name: "Finance", risk: 31, audits: 13 },
  { name: "AI Gov", risk: 57, audits: 9 },
  { name: "Commercial", risk: 72, audits: 21 }
];

export const trendData = [
  { week: "W1", flagged: 18, passed: 42, hours: 31 },
  { week: "W2", flagged: 14, passed: 51, hours: 44 },
  { week: "W3", flagged: 22, passed: 47, hours: 53 },
  { week: "W4", flagged: 11, passed: 63, hours: 68 },
  { week: "W5", flagged: 16, passed: 59, hours: 74 },
  { week: "W6", flagged: 9, passed: 71, hours: 91 }
];

export const breachData = [
  { name: "Notice", value: 32 },
  { name: "DPA", value: 27 },
  { name: "Security", value: 21 },
  { name: "Approval", value: 16 },
  { name: "Retention", value: 12 }
];

export const statusMeta = {
  Processing: { icon: FileSearch, className: "bg-slate-100 text-graphite border-slate-200" },
  "Analyzing Policies": { icon: Gavel, className: "bg-teal-50 text-teal border-teal/20" },
  Flagged: { icon: ShieldAlert, className: "bg-red-50 text-risk border-red-200" },
  Passed: { icon: CheckCircle2, className: "bg-emerald-50 text-ok border-emerald-200" }
} satisfies Record<AuditStatus, { icon: typeof AlertTriangle; className: string }>;
